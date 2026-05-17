namespace ToolEngine.Core.Domain.Entities;

using ToolEngine.Core.Abstractions.Audit;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Common;

public sealed class Tenant : AggregateRoot<string>, IAuditableEntity
{
    private readonly List<string> _allowedTools      = [];
    private readonly List<string> _allowedNamespaces = [];

    // Required by EF Core for materialization — never called by application code.
#pragma warning disable CS8618
    private Tenant() { }
#pragma warning restore CS8618

    private Tenant(
        string         tenantId,
        string         name,
        string         createdBy,
        DateTimeOffset createdAt)
        : base(tenantId)
    {
        Name      = name;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public string  Name                { get; private set; }
    public bool    IsActive            { get; private set; } = true;
    public string? LlmProviderOverride { get; private set; }
    /// <summary>Reference to a secret vault entry — never the raw API key.</summary>
    public string? LlmApiKeyRef        { get; private set; }
    public int     MaxResponseTokens   { get; private set; } = 25_000;
    public int     DailyToolCallBudget { get; private set; } = 10_000;

    public IReadOnlyList<string> AllowedTools      => _allowedTools.AsReadOnly();
    public IReadOnlyList<string> AllowedNamespaces => _allowedNamespaces.AsReadOnly();

    public DateTimeOffset  CreatedAt  { get; private set; }
    public string          CreatedBy  { get; private set; }
    public DateTimeOffset? UpdatedAt  { get; private set; }
    public string?         UpdatedBy  { get; private set; }

    /// <summary>Factory method — the only way to create a Tenant.</summary>
    public static Result<Tenant> Create(
        string            tenantId,
        string            name,
        string            createdBy,
        IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return Result.Failure<Tenant>(Error.Validation("TenantId cannot be empty."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Tenant>(Error.Validation("Tenant name cannot be empty."));

        var tenant = new Tenant(tenantId.Trim().ToLowerInvariant(), name.Trim(), createdBy,
                                clock.UtcNow);
        tenant.RaiseDomainEvent(new TenantCreatedEvent(tenant.Id, tenant.Name));
        return Result.Success(tenant);
    }

    /// <summary>Allow a specific tool. Name should be in "namespace.name" format.</summary>
    public Result AllowTool(string toolFullName)
    {
        if (_allowedTools.Contains(toolFullName, StringComparer.OrdinalIgnoreCase))
            return Result.Failure(Error.Conflict($"Tool '{toolFullName}' is already allowed."));
        _allowedTools.Add(toolFullName);
        return Result.Success();
    }

    /// <summary>Allow all tools in a namespace, e.g. "payment" or "weather".</summary>
    public void AllowNamespace(string ns) =>
        _allowedNamespaces.Add(ns.ToLowerInvariant());

    public void SetLlmProvider(string providerName, string? secretRef = null)
    {
        LlmProviderOverride = providerName;
        LlmApiKeyRef        = secretRef;  // reference to ISecretVault entry
    }

    public void SetLimits(int maxResponseTokens, int dailyBudget)
    {
        MaxResponseTokens   = maxResponseTokens;
        DailyToolCallBudget = dailyBudget;
    }

    public void Deactivate(string updatedBy, IDateTimeProvider clock)
    {
        IsActive  = false;
        UpdatedBy = updatedBy;
        UpdatedAt = clock.UtcNow;
    }
}

public sealed record TenantCreatedEvent(string TenantId, string Name) : DomainEvent;
