namespace ToolEngine.Tools.Samples.Database.UserLookup;

using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Base;

/// <summary>
/// Minimal user entity for the sample tool.
/// In a real project, User lives in ToolEngine.Core.Domain.Entities.
/// </summary>
public sealed class User : Entity<Guid>
{
    public User(Guid id, string email, string displayName,
                string tenantId, bool isActive) : base(id)
    {
        Email       = email;
        DisplayName = displayName;
        TenantId    = tenantId;
        IsActive    = isActive;
    }
    public string Email       { get; }
    public string DisplayName { get; }
    public string TenantId    { get; }
    public bool   IsActive    { get; }
}

/// <summary>
/// MCP name: "hr.user-lookup"
/// Database tool — looks up a user profile by UUID.
/// Demonstrates DatabaseToolBase with IReadRepository and tenant isolation.
/// </summary>
public sealed class UserLookupTool
    : DatabaseToolBase<UserLookupInput, UserLookupOutput, User, Guid>
{
    public UserLookupTool(IUnitOfWork uow, IReadRepository<User, Guid> repo)
        : base(uow, repo) { }

    public override string Namespace   => "hr";
    public override string Name        => "user-lookup";
    public override string Version     => "v1";
    public override string Description => "Retrieves a user profile by their UUID.";

    public override ToolSchema InputSchema => ToolSchema.For<UserLookupInput>(
        description:   "User identifier to look up.",
        whenToUse:     "Call when you need the display name, email, or active status of a user " +
                       "given their system UUID.",
        whenNotToUse:  "Do not call to search users by name or email — use hr.user-search instead. " +
                       "Do not call if you already have the user profile in context.",
        examples:
        [
            new("Look up user by UUID",
                new UserLookupInput(Guid.Parse("11111111-0000-0000-0000-000000000001")),
                new UserLookupOutput(
                    Guid.Parse("11111111-0000-0000-0000-000000000001"),
                    "alice@onebcg-default-tenant.com", "Alice Smith", "onebcg-default-tenant", true))
        ],
        new ToolParameter("userId", "string", "UUID of the user", Format: "uuid"));

    public override ToolSchema OutputSchema => ToolSchema.For<UserLookupOutput>(
        description:   "User profile. Email is sensitive — masked for unauthorised tenants.",
        whenToUse:     "Always returned on success.",
        whenNotToUse:  "N/A",
        examples:      [],
        new ToolParameter("userId",      "string",  "UUID"),
        new ToolParameter("email",       "string",  "Email address"),
        new ToolParameter("displayName", "string",  "Full display name"),
        new ToolParameter("tenantId",    "string",  "Tenant slug"),
        new ToolParameter("isActive",    "boolean", "Account active status"));

    public override async Task<ToolResponse<UserLookupOutput>> ExecuteAsync(
        ToolRequest<UserLookupInput> request,
        CancellationToken ct = default)
    {
        var user = await ReadRepository.GetByIdAsync(request.Input.UserId, ct);

        if (user is null)
            return ToolResponse<UserLookupOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound($"User '{request.Input.UserId}' not found."));

        // Tenant isolation — a tenant can only look up users in their own tenant
        if (!user.TenantId.Equals(request.TenantId, StringComparison.OrdinalIgnoreCase))
            return ToolResponse<UserLookupOutput>.Fail(
                request.CorrelationId,
                ToolError.FromError(
                    Error.TenantNotAllowed(request.TenantId, FullName), 403));

        return ToolResponse<UserLookupOutput>.Ok(
            request.CorrelationId,
            new UserLookupOutput(
                user.Id, user.Email, user.DisplayName, user.TenantId, user.IsActive));
    }
}
