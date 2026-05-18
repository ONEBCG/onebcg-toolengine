---
name: toolengine-phase1-core
description: >
  Scaffolds Phase 1 of ToolEngine v2026: the two foundational projects
  Core.Abstractions and Core.Domain. Pure interfaces, DDD building blocks,
  Result<T> railway pattern, ToolRequest/ToolResponse contracts, all domain
  entities including CallerType, GovernanceMetadataJson, RetainUntil,
  OutboxMessage, and AcknowledgementStatement. Zero infrastructure dependencies.
classification: Confidential - Internal Use Only
---

# Phase 1 — Core Abstractions + Domain

## What this phase produces

Two class library projects with **zero business logic and zero infrastructure
dependencies**. Everything here is either a pure interface or an immutable domain
type. All other projects depend on these; these depend on nothing except the .NET BCL.

```
src/
  Core/
    ToolEngine.Core.Abstractions/    ← pure interfaces only, no NuGet dependencies
    ToolEngine.Core.Domain/          ← entities, VOs, Result<T>, unified contracts
```

---

## Step-by-step scaffold

```bash
dotnet new sln -n ToolEngine
dotnet new classlib -n ToolEngine.Core.Abstractions \
  -o src/Core/ToolEngine.Core.Abstractions --framework net8.0
dotnet new classlib -n ToolEngine.Core.Domain \
  -o src/Core/ToolEngine.Core.Domain --framework net8.0
dotnet sln add src/Core/ToolEngine.Core.Abstractions
dotnet sln add src/Core/ToolEngine.Core.Domain
dotnet add src/Core/ToolEngine.Core.Domain reference \
  src/Core/ToolEngine.Core.Abstractions
rm src/Core/ToolEngine.Core.Abstractions/Class1.cs
rm src/Core/ToolEngine.Core.Domain/Class1.cs
```

---

## Shared .csproj settings (both projects)

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <LangVersion>latest</LangVersion>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

**Core.Abstractions:** NO `<PackageReference>` entries — BCL only.

**Core.Domain:** add only:
```xml
<PackageReference Include="System.Text.Json" Version="8.*" />
```

---

## File layout — ToolEngine.Core.Abstractions

```
Common/
  IDateTimeProvider.cs       — DateTimeOffset UtcNow { get; }
  ICurrentUser.cs            — string? UserId { get; }
Persistence/
  IRepository.cs             — AddAsync, UpdateAsync, DeleteAsync
  IReadRepository.cs         — GetByIdAsync, ListAsync, PagedListAsync
  IUnitOfWork.cs             — SaveChangesAsync(ct); extends IAsyncDisposable
Cache/
  ICacheProvider.cs          — GetAsync, SetAsync, RemoveAsync, IncrementAsync
Secrets/
  ISecretVault.cs            — GetSecretAsync(scope, name, key, ct)
```

### IUnitOfWork.cs
```csharp
namespace ToolEngine.Core.Abstractions.Persistence;

public interface IUnitOfWork : IAsyncDisposable
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

### ICacheProvider.cs
```csharp
namespace ToolEngine.Core.Abstractions.Cache;

public interface ICacheProvider
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    // IncrementAsync is required for distributed loop detection (Phase F4)
    Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? expiry = null, CancellationToken ct = default);
}
```

### IReadRepository.cs
```csharp
namespace ToolEngine.Core.Abstractions.Persistence;

public interface IReadRepository<TEntity, TId>
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<PagedResult<TEntity>> PagedListAsync(
        ISpecification<TEntity> spec, int pageNumber, int pageSize, CancellationToken ct = default);
}
```

---

## File layout — ToolEngine.Core.Domain

```
Common/
  Result.cs                  — Result<T> + non-generic Result; railway-oriented
  Error.cs                   — SCREAMING_SNAKE_CASE codes, factory methods
  Entity.cs                  — Entity<TId>: Id, CreatedAt, UpdatedAt
  AggregateRoot.cs           — AggregateRoot<TId>: DomainEvents, ClearDomainEvents()
  ValueObject.cs             — structural equality via GetEqualityComponents()
  DomainEvent.cs             — record DomainEvent(Guid Id, DateTimeOffset OccurredAt)
  PagedResult.cs             — PagedResult<T>: Items, TotalCount, TotalPages, HasNext, HasPrevious
Contracts/
  ToolRequest.cs             — ToolRequest<TInput> record — all invocation fields
  ToolResponse.cs            — ToolResponse<TOutput> record + IToolResponse interface
  ToolError.cs               — HTTP status + ErrorCode + Description
  ToolUsageMetrics.cs        — DurationMs(long), TokensUsed(int)
  ToolChunk.cs               — streaming chunk: CorrelationId, Content, Index, IsFinal
  AcknowledgementStatement.cs — EU AI Act Article 14 evidence record (H3)
Enums/
  ExecutionMode.cs           — Sequential, Parallel, Dag
  ToolType.cs                — Logic, Api, Database, Composite
  ToolStatus.cs              — Pending, Running, Succeeded, Failed, Suspended
  ApprovalStatus.cs          — Pending, Approved, Denied, Expired
  ApprovalChannel.cs         — Dashboard, EmailMagicLink, EmailOtp, Webhook
  ApprovalRisk.cs            — Low, Medium, High, Critical
  CallerType.cs              — Human, AiAgent, SystemService  ← H4, ISO 42001
Entities/
  Tenant.cs                  — AggregateRoot<string> — full config entity
  ToolInvocationRecord.cs    — AggregateRoot<Guid> — audit record with H2/H4/H5 fields
  ToolInvocationEvent.cs     — Entity<Guid> — append-only audit event (H1)
  PendingApproval.cs         — Entity<Guid> — approval lifecycle with E1/E2 controls
  OutboxMessage.cs           — Entity<Guid> — reliable notification delivery (F7)
```

---

## Key code patterns

### Result\<T\> — railway-oriented programming

```csharp
namespace ToolEngine.Core.Domain.Common;

public sealed class Result<T>
{
    private readonly T?    _value;
    private readonly Error? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Result is a failure.");
    public Error Error => IsFailure ? _error! : throw new InvalidOperationException("Result is a success.");

    private Result(T value)     { _value = value; IsSuccess = true; }
    private Result(Error error) { _error = error; IsSuccess = false; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> f) =>
        IsSuccess ? Result<TOut>.Success(f(Value)) : Result<TOut>.Failure(Error);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> f) =>
        IsSuccess ? f(Value) : Result<TOut>.Failure(Error);
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}
```

### Error.cs

```csharp
namespace ToolEngine.Core.Domain.Common;

public sealed record Error(string Code, string Description)
{
    public static Error NotFound(string entity, string id) =>
        new("NOT_FOUND", $"{entity} '{id}' was not found.");
    public static Error Validation(string description) =>
        new("VALIDATION_ERROR", description);
    public static Error Unauthorized(string description) =>
        new("UNAUTHORIZED", description);
    public static Error Conflict(string description) =>
        new("CONFLICT", description);
    public static Error ApprovalPending(Guid invocationId) =>
        new("APPROVAL_PENDING", $"Invocation '{invocationId}' is pending human approval.");
    public static Error InvalidOtp() =>
        new("INVALID_OTP", "The OTP provided is incorrect or has expired.");
    public static Error InvalidApprovalToken() =>
        new("INVALID_APPROVAL_TOKEN", "The approval token is invalid or has expired.");
}
```

### ToolRequest\<TInput\>

```csharp
namespace ToolEngine.Core.Domain.Contracts;

public sealed record ToolRequest<TInput>(
    Guid    CorrelationId,
    string  TenantId,
    string  ToolName,
    string  ToolVersion,
    TInput  Input,
    ExecutionMode Mode         = ExecutionMode.Sequential,
    bool    Streaming          = false,
    string? UserId             = null,
    Dictionary<string, string>? Metadata = null,
    int     MaxResponseTokens  = 0,
    string? ResponseFormat     = null,
    string? ToolNamespace      = null)
{
    public string FullName => string.IsNullOrEmpty(ToolNamespace)
        ? ToolName
        : $"{ToolNamespace}.{ToolName}";
}
```

### ToolResponse\<TOutput\> + IToolResponse

```csharp
namespace ToolEngine.Core.Domain.Contracts;

// Non-generic interface used by AuditBehavior to inspect response without generic constraints
public interface IToolResponse
{
    bool       Success { get; }
    bool       IsSuspended { get; }
    Guid?      PendingInvocationId { get; }
    ToolError? Error { get; }
}

public sealed record ToolResponse<TOutput>(
    Guid             CorrelationId,
    bool             Success,
    TOutput?         Data,
    ToolError?       Error,
    ToolUsageMetrics Metrics,
    DateTimeOffset   Timestamp,
    bool             IsSuspended         = false,
    Guid?            PendingInvocationId = null) : IToolResponse
{
    public static ToolResponse<TOutput> Ok(Guid correlationId, TOutput data, ToolUsageMetrics? metrics = null) =>
        new(correlationId, true, data, null, metrics ?? new(0, 0), DateTimeOffset.UtcNow);

    public static ToolResponse<TOutput> Fail(Guid correlationId, ToolError error) =>
        new(correlationId, false, default, error, new(0, 0), DateTimeOffset.UtcNow);

    public static ToolResponse<TOutput> Suspended(Guid correlationId, Guid pendingInvocationId) =>
        new(correlationId, false, default, null, new(0, 0), DateTimeOffset.UtcNow,
            IsSuspended: true, PendingInvocationId: pendingInvocationId);
}
```

### AcknowledgementStatement (H3 — EU AI Act Article 14)

```csharp
namespace ToolEngine.Core.Domain.Contracts;

public sealed record AcknowledgementStatement(
    string         RegBasis,          // "EU AI Act Article 14 §4"
    string         RiskLevel,         // "High" | "Critical"
    string         ToolFullName,
    string         OperatorStatement,
    DateTimeOffset IssuedAt);
```

### CallerType enum (H4 — AI agent identity)

```csharp
namespace ToolEngine.Core.Domain.Enums;

public enum CallerType
{
    Human,          // default — human operator via UI or API
    AiAgent,        // LLM-initiated tool call (Phase L AgentOrchestrator)
    SystemService   // internal automated system call
}
```

---

## Entity patterns

### Tenant.cs

```csharp
namespace ToolEngine.Core.Domain.Entities;

public sealed class Tenant : AggregateRoot<string>
{
    public string Name              { get; private set; } = default!;
    public bool   IsActive          { get; private set; } = true;
    public string CreatedBy         { get; private set; } = default!;
    public int    DailyToolCallBudget { get; private set; }   // 0 = no cap
    public int    MaxResponseTokens   { get; private set; }   // 0 = no cap
    public string? LlmProvider        { get; private set; }
    public string? LlmProviderOverride { get; private set; }

    private readonly List<string> _allowedNamespaces = new();
    public IReadOnlyList<string> AllowedNamespaces => _allowedNamespaces.AsReadOnly();

    private Tenant() { }  // EF Core constructor

    public static Result<Tenant> Create(string id, string name, string createdBy, IDateTimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result<Tenant>.Failure(Error.Validation("TenantId cannot be empty."));
        var tenant = new Tenant
        {
            Id        = id.Trim().ToLowerInvariant(),  // always lowercase
            Name      = name,
            CreatedBy = createdBy,
            CreatedAt = clock.UtcNow
        };
        return Result<Tenant>.Success(tenant);
    }

    public void AllowNamespace(string ns) => _allowedNamespaces.Add(ns);

    // F6 — deny-by-default: empty list means NO access, not unrestricted
    public bool IsNamespaceAllowed(string ns)
    {
        if (_allowedNamespaces.Count == 0) return false;           // deny all
        if (_allowedNamespaces.Contains("*")) return true;         // explicit wildcard
        return _allowedNamespaces.Contains(ns, StringComparer.OrdinalIgnoreCase);
    }

    public void SetLimits(int maxResponseTokens, int dailyBudget)
    {
        MaxResponseTokens  = maxResponseTokens;
        DailyToolCallBudget = dailyBudget;
    }

    public void SetLlmProvider(string provider, string? secretRef = null)
    {
        LlmProvider         = provider;
        LlmProviderOverride = secretRef;
    }
}
```

### ToolInvocationRecord.cs (H2 / H4 / H5)

```csharp
namespace ToolEngine.Core.Domain.Entities;

public sealed class ToolInvocationRecord : AggregateRoot<Guid>
{
    public string      TenantId              { get; private set; } = default!;
    public string?     UserId                { get; private set; }
    public string      ToolFullName          { get; private set; } = default!;
    public string      ToolVersion           { get; private set; } = default!;
    public ToolStatus  Status                { get; private set; }
    public DateTimeOffset InvokedAt          { get; private set; }
    public DateTimeOffset? CompletedAt       { get; private set; }
    public long?       DurationMs            { get; private set; }
    public string?     ErrorCode             { get; private set; }
    public string?     ErrorMessage          { get; private set; }
    public int         TokensUsed            { get; private set; }
    public CallerType  CallerType            { get; private set; }  // H4
    public string?     GovernanceMetadataJson { get; private set; } // H5
    public DateTimeOffset RetainUntil        { get; private set; }  // H2 GDPR
    public bool        IsAnonymized          { get; private set; }  // H2

    private ToolInvocationRecord() { }

    public static ToolInvocationRecord Create(
        Guid correlationId, string tenantId, string? userId,
        string toolFullName, string toolVersion,
        CallerType callerType, string? governanceMetadataJson,
        IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        return new ToolInvocationRecord
        {
            Id                    = correlationId,
            TenantId              = tenantId,
            UserId                = userId,
            ToolFullName          = toolFullName,
            ToolVersion           = toolVersion,
            Status                = ToolStatus.Running,
            InvokedAt             = now,
            CallerType            = callerType,
            GovernanceMetadataJson = governanceMetadataJson,
            RetainUntil           = now.AddDays(90),  // H2: 90-day retention default
            IsAnonymized          = false
        };
    }

    public void MarkSucceeded(ToolUsageMetrics metrics)
    {
        Status      = ToolStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs  = metrics.DurationMs;
        TokensUsed  = metrics.TokensUsed;
    }

    public void MarkFailed(ToolError error)
    {
        Status       = ToolStatus.Failed;
        CompletedAt  = DateTimeOffset.UtcNow;
        ErrorCode    = error.ErrorCode;
        ErrorMessage = error.Description;
    }

    // H2: GDPR right-to-erasure — nulls PII, retains structural fields for SOC 2
    public void Anonymize()
    {
        if (IsAnonymized) return;  // idempotent
        UserId                = "[anonymized]";
        ErrorMessage          = null;
        GovernanceMetadataJson = null;  // may contain model/session PII
        IsAnonymized          = true;
    }
}
```

### ToolInvocationEvent.cs (H1 — append-only)

```csharp
namespace ToolEngine.Core.Domain.Entities;

// APPEND-ONLY — no update methods. Used as the SOC 2 immutable audit log.
public sealed class ToolInvocationEvent : Entity<Guid>
{
    public Guid       InvocationRecordId    { get; private set; }
    public string     EventType             { get; private set; } = default!; // "Invoked"|"Succeeded"|"Failed"|"Suspended"
    public DateTimeOffset OccurredAt        { get; private set; }
    public long?      DurationMs            { get; private set; }
    public CallerType CallerType            { get; private set; }  // H4 on every row
    public string?    GovernanceMetadataJson { get; private set; } // H5 on every row

    private ToolInvocationEvent() { }

    public static ToolInvocationEvent Create(
        Guid invocationRecordId, string eventType,
        CallerType callerType, string? governanceMetadataJson,
        long? durationMs, IDateTimeProvider clock) =>
        new ToolInvocationEvent
        {
            Id                    = Guid.NewGuid(),
            InvocationRecordId    = invocationRecordId,
            EventType             = eventType,
            OccurredAt            = clock.UtcNow,
            DurationMs            = durationMs,
            CallerType            = callerType,
            GovernanceMetadataJson = governanceMetadataJson
        };
    // NO Update/Modify methods — Create() only
}
```

### PendingApproval.cs (E1 / E2 / F8 / H3)

```csharp
namespace ToolEngine.Core.Domain.Entities;

public sealed class PendingApproval : Entity<Guid>
{
    public string         TenantId        { get; private set; } = default!;
    public string         ToolFullName    { get; private set; } = default!;
    public string         ApprovalToken   { get; private set; } = default!; // E1: 256-bit CSPRNG
    public string?        OtpHash         { get; private set; }
    public ApprovalStatus Status          { get; private set; }
    public ApprovalRisk   Risk            { get; private set; }
    public ApprovalChannel Channel        { get; private set; }
    public DateTimeOffset ExpiresAt       { get; private set; }
    public int            FailedOtpAttempts { get; private set; } // E2
    public string?        IdempotencyKey  { get; private set; }   // F8
    public string?        AcknowledgementJson { get; private set; } // H3

    private PendingApproval() { }

    public static PendingApproval Create(
        string tenantId, string toolFullName,
        ApprovalRisk risk, ApprovalChannel channel,
        string? idempotencyKey, IDateTimeProvider clock)
    {
        return new PendingApproval
        {
            Id             = Guid.NewGuid(),
            TenantId       = tenantId,
            ToolFullName   = toolFullName,
            // E1: 256-bit CSPRNG token (not Guid.NewGuid) — exceeds OWASP 128-bit minimum by 2x
            ApprovalToken  = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Status         = ApprovalStatus.Pending,
            Risk           = risk,
            Channel        = channel,
            ExpiresAt      = clock.UtcNow.AddMinutes(60),
            IdempotencyKey = idempotencyKey
        };
    }

    // E2: per-token OTP lockout; returns true when approval should be expired
    public bool IncrementFailedOtpAttempts(int maxAttempts = 5)
    {
        FailedOtpAttempts++;
        if (FailedOtpAttempts >= maxAttempts)
        {
            Status = ApprovalStatus.Expired;
            return true;
        }
        return false;
    }

    // H3: EU AI Act Article 14 acknowledgement payload
    public void SetAcknowledgement(string json) =>
        AcknowledgementJson ??= json;  // immutable once set
}
```

### OutboxMessage.cs (F7 — reliable notification delivery)

```csharp
namespace ToolEngine.Core.Domain.Entities;

public sealed class OutboxMessage : Entity<Guid>
{
    public string         MessageType { get; private set; } = default!;
    public string         Payload     { get; private set; } = default!;
    public DateTimeOffset CreatedAt   { get; private set; }
    public DateTimeOffset? SentAt    { get; private set; }
    public int            RetryCount  { get; private set; }
    public string?        Error       { get; private set; }

    private OutboxMessage() { }

    public void MarkSent()             { SentAt = DateTimeOffset.UtcNow; }
    public void MarkFailed(string err) { RetryCount++; Error = err; }
}
```

---

## Naming conventions

| Element | Convention |
|---|---|
| Interfaces | `I` prefix, noun or noun-phrase |
| Immutable data | `record` with primary constructor |
| Enums | Singular noun, PascalCase values |
| `Result` factory methods | `Result.Success(value)`, `Result.Failure(error)` |
| Error codes | `SCREAMING_SNAKE_CASE` string, e.g. `"TOOL_NOT_FOUND"` |
| Folder → namespace | `ToolEngine.Core.Domain.Common`, `.Contracts`, `.Entities`, etc. |
| Tenant ID | Always lowercase slug — enforced in `Tenant.Create()` |

---

## Phase 1 completion checklist

- [ ] `dotnet build` zero warnings, zero errors on both projects
- [ ] `Core.Abstractions` has NO `<PackageReference>` entries
- [ ] `Core.Domain` references only `Core.Abstractions` + `System.Text.Json`
- [ ] `Result<T>` — `IsSuccess` and `IsFailure` are mutually exclusive
- [ ] `ToolRequest<TInput>` has: `CorrelationId`, `TenantId`, `ToolNamespace`, `UserId`, `Metadata`, `MaxResponseTokens`
- [ ] `IToolResponse` non-generic interface exists; `ToolResponse<T>` implements it
- [ ] `CallerType` enum: `Human`, `AiAgent`, `SystemService`
- [ ] `ICacheProvider.IncrementAsync` method present (required by Phase F4 loop detection)
- [ ] `IUnitOfWork` extends `IAsyncDisposable` (not `IDisposable`)
- [ ] `ToolInvocationEvent` has NO update methods — `Create()` factory only
- [ ] `Tenant.Create()` lowercases the ID (`.Trim().ToLowerInvariant()`)
- [ ] `Tenant.IsNamespaceAllowed()` returns `false` when `AllowedNamespaces.Count == 0` (deny-by-default, F6)
- [ ] `PendingApproval.ApprovalToken` uses `RandomNumberGenerator.GetBytes(32)` (E1)
- [ ] `PendingApproval.IncrementFailedOtpAttempts()` expires approval at max failures (E2)
- [ ] `ToolInvocationRecord.RetainUntil = InvokedAt + 90 days` (H2)
- [ ] `ToolInvocationRecord.Anonymize()` nulls `UserId`, `ErrorMessage`, `GovernanceMetadataJson` (H2 + H5)
- [ ] `AcknowledgementStatement` record with all required fields (H3)
- [ ] `OutboxMessage` entity present (needed by Phase F7)
- [ ] `PagedResult<T>` in `Common/` with `TotalPages`, `HasNext`, `HasPrevious` (F9)

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
