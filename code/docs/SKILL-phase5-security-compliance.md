---
name: toolengine-phase5-security-compliance
description: >
  Implements all security hardening (Phase E), provider abstractions (Phase F),
  OpenTelemetry observability (Phase G), and compliance controls (Phase H) for
  ToolEngine v2026. Covers: CSPRNG tokens, OTP lockout+rate-limiting, JWT key
  validation, pipeline ordering, modular DB/cache, outbox pattern, idempotency,
  deny-by-default namespaces, distributed loop detection, OTel tracing/metrics,
  W3C TraceContext, Serilog PII masking, append-only audit log, GDPR anonymisation,
  EU AI Act Article 14 acknowledgements, CallerType agent identity, and
  ISO 42001 GovernanceMetadataJson.
classification: Confidential - Internal Use Only
---

# Phase 5 — Security Hardening + Provider Abstractions + Observability + Compliance

## Prerequisites

Phases 1–4 complete. Zero-warning build on all projects.

---

## Overview

| Phase | Items | Standards |
|---|---|---|
| E — Security Hardening | 7 | OWASP Top 10 2025, OWASP MFA, OWASP Secrets Management |
| F — Provider Abstractions | 9 | AWS SaaS guidance, Redis patterns, Azure Async Request-Reply |
| G — Observability | 4 | OpenTelemetry spec, W3C TraceContext, Serilog best practices |
| H — Compliance | 5 | SOC 2 CC6/CC7, GDPR Articles 5/17, EU AI Act Article 14, NIST AI RMF, ISO 42001 |

This phase touches files across all projects — it augments rather than replaces Phase 3 and 4 implementations.

---

## Phase E — Security Hardening

### E1 — CSPRNG approval token

**File:** `src/Core/ToolEngine.Core.Domain/Entities/PendingApproval.cs`

Replace `Guid.NewGuid()` token generation with a cryptographically secure random token.
`Guid.NewGuid()` provides ~122 bits of entropy and has a predictable UUID v4 structure.
OWASP minimum is 128 bits; we use 256 bits (2× minimum).

```csharp
// Before (insecure):
ApprovalToken = Guid.NewGuid().ToString("N");  // 122-bit, predictable structure

// After (E1 — 256-bit CSPRNG, 64-char hex string):
ApprovalToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
// RandomNumberGenerator is in System.Security.Cryptography — no NuGet package needed
```

### E2 — OTP lockout on max failed attempts

**File:** `src/Core/ToolEngine.Core.Domain/Entities/PendingApproval.cs`

```csharp
public int FailedOtpAttempts { get; private set; }

/// <returns>true when the approval has been expired due to too many failures</returns>
public bool IncrementFailedOtpAttempts(int maxAttempts = 5)
{
    FailedOtpAttempts++;
    if (FailedOtpAttempts >= maxAttempts)
    {
        Status = ApprovalStatus.Expired;
        return true;   // caller: persist and reject
    }
    return false;
}
```

**File:** `src/Hosts/ToolEngine.Api/Endpoints/ApprovalEndpoints.cs`

```csharp
var expired = pending.IncrementFailedOtpAttempts(approvalOpts.OtpMaxFailedAttempts);
await _db.SaveChangesAsync(ct);

if (expired)
    return Results.BadRequest(new { error = "OTP_LOCKED_OUT",
        description = "Too many failed OTP attempts. This approval has been invalidated." });

return Results.BadRequest(new { error = "INVALID_OTP",
    remainingAttempts = approvalOpts.OtpMaxFailedAttempts - pending.FailedOtpAttempts });
```

### E3 — OTP rate limiting (IP-level) + PBKDF2-HMAC-SHA256 hashing

**Rate limiting** (in `Program.cs` — see Phase 4 §7 for full snippet):
- Sliding window: 10 attempts per IP per 10 minutes
- `Retry-After: 60` on rejection
- Apply with `.RequireRateLimiting("otp-verify")` on the OTP verify endpoint

**OTP generation and hashing:**

```csharp
// Generate
var otp  = Random.Shared.Next(100_000, 999_999).ToString();
var salt = RandomNumberGenerator.GetBytes(16);
using var pbkdf2 = new Rfc2898DeriveBytes(otp, salt, 100_000, HashAlgorithmName.SHA256);
var hash = pbkdf2.GetBytes(32);
pending.OtpHash = $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";

// Verify (constant-time — prevents timing oracle attacks)
var parts = pending.OtpHash.Split(':');
var storedSalt = Convert.FromHexString(parts[0]);
using var verify = new Rfc2898DeriveBytes(submittedOtp, storedSalt, 100_000, HashAlgorithmName.SHA256);
var computed = verify.GetBytes(32);
var stored   = Convert.FromHexString(parts[1]);
// CryptographicOperations.FixedTimeEquals — NOT computed == stored (timing-safe)
return CryptographicOperations.FixedTimeEquals(computed, stored);
```

### E4 — Pipeline order: TenantAuth BEFORE Validation

**File:** `src/Application/ToolEngine.Application/Extensions/ServiceCollectionExtensions.cs`

Authorization must be the outermost pipeline behavior. Without this, an unauthenticated
caller receives `400 VALIDATION_ERROR` with field-level details before any auth check runs
— disclosing internal schema to attackers (OWASP A01:2025 Broken Access Control).

```csharp
// Registration order = execution order (first registered = outermost wrapper):
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantAuthorizationBehavior<,>)); // 1st
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));          // 2nd
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TokenBudgetBehavior<,>));         // 3rd
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DailyBudgetBehavior<,>));         // 4th
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoopDetectionBehavior<,>));       // 5th
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ApprovalBehavior<,>));            // 6th
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));               // 7th (innermost)
```

### E5 — Enforce DailyToolCallBudget

**File:** `src/Application/ToolEngine.Application/Behaviors/DailyBudgetBehavior.cs`

`DailyToolCallBudget` was defined on the `Tenant` entity from Phase 1 but never checked.
This behavior enforces it.

```csharp
if (tenant.DailyToolCallBudget <= 0) return await next();  // 0 or negative = no cap

// Use UTC midnight boundary — not current time — to count today's invocations
var startOfDayUtc = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime)
    .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

var todayCount = await _db.Set<ToolInvocationRecord>()
    .CountAsync(r => r.TenantId == command.TenantId
                  && r.InvokedAt >= startOfDayUtc, ct);

if (todayCount >= tenant.DailyToolCallBudget)
{
    ToolEngineTelemetry.DailyBudgetExceeded.Add(1, new("tenant", command.TenantId));
    return ToolResponse.Fail(ToolError.FromError(
        Error.Validation("DAILY_BUDGET_EXCEEDED"), 429));
}
return await next();
```

### E6 — 202 response with Location + Retry-After headers

**File:** `src/Hosts/ToolEngine.Api/Endpoints/ToolEndpoints.cs`

RFC 7231 §6.3.3: "202 Accepted" SHOULD include a Location header pointing to a status endpoint.
Microsoft Azure Async Request-Reply pattern also requires `Retry-After`.

```csharp
if (response.IsSuspended)
{
    var pollUrl = $"/invocations/{response.PendingInvocationId}/status";
    // Set Retry-After explicitly — Results.Accepted() does not set it automatically
    ctx.Response.Headers["Retry-After"] = "10";
    // The location parameter to Results.Accepted() sets the Location response header
    return Results.Accepted(pollUrl, new {
        status        = "pending_approval",
        invocationId  = response.PendingInvocationId,
        pollUrl,
        message       = "Tool execution is suspended pending human approval."
    });
}
```

### E7 — JWT key length + Approval HTTPS startup validation

**File:** `src/Hosts/ToolEngine.Api/Program.cs`

Fail fast before accepting any traffic:

```csharp
// JWT minimum key length: 256 bits (32 bytes) for HMAC-SHA256
if (Encoding.UTF8.GetBytes(jwt.Secret).Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be at least 32 bytes (256 bits). " +
        "Generate a secure key: openssl rand -base64 32");

// Approval BaseUrl must use HTTPS in non-development
// Magic links sent over HTTP are vulnerable to interception (MITM)
if (!app.Environment.IsDevelopment())
{
    var opts = app.Services.GetRequiredService<IOptions<ApprovalOptions>>().Value;
    if (!opts.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "Approval:BaseUrl must use HTTPS in non-development environments.");
}
```

---

## Phase F — Provider Abstractions

### F1 — Modular database provider

**File:** `src/Hosts/ToolEngine.Api/Program.cs`

```csharp
// Config: "Database": { "Provider": "sqlite" | "postgresql" | "sqlserver" }
builder.Services.AddToolInfrastructure(opt => {
    switch (dbOpts.Provider.ToLowerInvariant()) {
        case "postgresql": opt.UseNpgsql(connStr); break;
        case "sqlserver":  opt.UseSqlServer(connStr); break;
        default:           opt.UseSqlite(connStr); break;  // sqlite = default
    }
});
```

Required NuGet packages in `ToolEngine.Infrastructure`:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.*" />
```

### F2 — EF Core migrations in production

```csharp
// Development: always rebuild (ephemeral data, prevents stale schema)
await db.Database.EnsureDeletedAsync();
await db.Database.EnsureCreatedAsync();

// Production: apply pending migrations (preserves data)
await db.Database.MigrateAsync();
```

**Create initial migration:**
```bash
dotnet ef migrations add InitialCreate \
  --project src/Infrastructure/ToolEngine.Infrastructure \
  --startup-project src/Hosts/ToolEngine.Api

dotnet ef database update \
  --startup-project src/Hosts/ToolEngine.Api
```

### F3 — ICacheProvider abstraction

Defined in Phase 1 `Core.Abstractions`. Two implementations in Infrastructure:

**MemoryCacheProvider** (dev/single-node — registered as fallback by `AddToolInfrastructure`):
```csharp
// Wraps IMemoryCache. IncrementAsync is optimistic get/increment/set.
services.AddMemoryCache().AddSingleton<ICacheProvider, MemoryCacheProvider>();
```

**DistributedCacheProvider** (Redis — register BEFORE AddToolInfrastructure to take precedence):
```csharp
// In Program.cs — must come before AddToolInfrastructure:
builder.Services.AddStackExchangeRedisCache(opt => opt.Configuration = redisConnStr);
builder.Services.AddDistributedCacheProvider();  // registers DistributedCacheProvider
// AddToolInfrastructure checks: if ICacheProvider already registered, skip memory fallback
```

**Config:**
```json
{ "Cache": { "Provider": "redis" }, "ConnectionStrings": { "Redis": "host:6379" } }
```

### F4 — Distributed loop detection

**File:** `src/Application/ToolEngine.Application/Behaviors/LoopDetectionBehavior.cs`

```csharp
// Key: "{correlationId}:{toolFullName}" — independent counters per correlation+tool pair
var key   = $"{command.CorrelationId}:{command.FullName}";
// TTL = 10 minutes sliding — prevents unbounded memory accumulation (no static ConcurrentDictionary)
var count = await _cache.IncrementAsync(key, 1, expiry: TimeSpan.FromMinutes(10), ct);

if (count > _options.MaxCallsPerCorrelation)  // configurable, default 10
{
    await _cache.RemoveAsync(key, ct);  // clean state
    ToolEngineTelemetry.LoopDetections.Add(1, new("tool", command.FullName));
    return ToolResponse.Fail(ToolError.FromError(Error.Validation("LOOP_DETECTED"), 429));
}
return await next();
```

With Redis `ICacheProvider`, counter is shared across all API pods — safe for horizontal scale.
With memory provider, counter is per-process — dev/single-node only.

**Never use `static ConcurrentDictionary`** — grows indefinitely, not distributed-safe.

### F5 — Scoped tenant cache (eliminates double DB read)

Two behaviors load the same `Tenant` per request (TenantAuthorizationBehavior +
TokenBudgetBehavior). `CachedTenantReadRepository` caches the result per HTTP request scope:

```csharp
// Scoped registration — cache lives for one HTTP request
services.AddScoped<IReadRepository<Tenant, string>, CachedTenantReadRepository>();

// Implementation: Dictionary<string, Tenant?> per instance
public async Task<Tenant?> GetByIdAsync(string id, CancellationToken ct = default)
{
    if (_cache.TryGetValue(id, out var hit)) return hit;  // in-request cache hit
    return _cache[id] = await _inner.GetByIdAsync(id, ct);
}
```

### F6 — Deny-by-default namespace allowlist

**File:** `src/Core/ToolEngine.Core.Domain/Entities/Tenant.cs`

```csharp
// DENY-BY-DEFAULT: empty list = no access (not unrestricted access)
// This is the AWS SaaS guidance and Zero Trust default
public bool IsNamespaceAllowed(string ns)
{
    if (_allowedNamespaces.Count == 0) return false;           // deny all
    if (_allowedNamespaces.Contains("*")) return true;         // explicit wildcard
    return _allowedNamespaces.Contains(ns, StringComparer.OrdinalIgnoreCase);
}

// Dev tenants must explicitly grant wildcard:
devTenant.AllowNamespace("*");  // required in all dev seeds
```

A newly created tenant has access to NOTHING until explicitly granted. Never default to unrestricted access.

### F7 — Outbox pattern for approval notifications

**Why:** Without an outbox, approval creation and notification delivery are in separate transactions.
If the process crashes between them, the approver is never notified.

```csharp
// AsyncApprovalGate: write PendingApproval + OutboxMessage in ONE transaction
await _db.Set<PendingApproval>().AddAsync(pending, ct);
await _db.Set<OutboxMessage>().AddAsync(new OutboxMessage {
    MessageType = "approval.notify",
    Payload     = JsonSerializer.Serialize(context),
    CreatedAt   = _clock.UtcNow
}, ct);
await _db.SaveChangesAsync(ct);  // both written or neither — atomic
```

**NotificationDispatchService** (`IHostedService`):
- Polls `OutboxMessage WHERE SentAt IS NULL AND RetryCount < 5` every 15 seconds
- Delivers via `IApprovalChannel.SendAsync`
- Exponential backoff on failure: 30s → 2m → 8m → 32m → 2h
- Abandons after `RetryCount >= 5` (records `Error`, stops retrying — no infinite loops)

### F8 — Idempotency key

**File:** `src/Hosts/ToolEngine.Api/Endpoints/ToolEndpoints.cs`

```csharp
var idempotencyKey = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault()
    ?? Guid.NewGuid().ToString();  // generate if absent
```

**File:** `src/Infrastructure/ToolEngine.Infrastructure/Approval/AsyncApprovalGate.cs`

```csharp
// Check for existing non-expired approval with same key + tenant before creating new one
var existing = await _db.Set<PendingApproval>()
    .FirstOrDefaultAsync(p => p.IdempotencyKey == context.IdempotencyKey
                            && p.TenantId == context.TenantId
                            && p.Status == ApprovalStatus.Pending
                            && p.ExpiresAt > _clock.UtcNow, ct);
if (existing is not null)
    return ApprovalDecision.Suspended(existing.Id);  // no duplicate created
```

### F9 — Pagination on IReadRepository

**File:** `src/Core/ToolEngine.Core.Abstractions/Persistence/IReadRepository.cs`

```csharp
Task<PagedResult<T>> PagedListAsync(
    ISpecification<T> spec, int pageNumber, int pageSize, CancellationToken ct = default);
```

**File:** `src/Core/ToolEngine.Core.Domain/Common/PagedResult.cs`

```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              TotalCount,
    int              PageNumber,
    int              PageSize)
{
    public int  TotalPages  => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext     => PageNumber < TotalPages;
    public bool HasPrevious => PageNumber > 1;
}
```

---

## Phase G — Observability

### G1 — OpenTelemetry tracing

**File:** `src/Application/ToolEngine.Application/Telemetry/ToolEngineTelemetry.cs`

```csharp
public static class ToolEngineTelemetry
{
    public const string ServiceName    = "ToolEngine";
    public const string ServiceVersion = "2026.1.0";
    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter          Meter          = new(ServiceName);
}
```

**Usage in AuditBehavior:**
```csharp
using var activity = ToolEngineTelemetry.ActivitySource.StartActivity("tool.execute");
activity?.SetTag("tool.full_name", command.FullName);
activity?.SetTag("tenant.id", command.TenantId);
activity?.SetTag("caller.type", command.CallerType.ToString());  // H4 on span
activity?.SetTag("tool.version", command.ToolVersion);
```

**Registration in Program.cs:**
```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ToolEngineTelemetry.ServiceName, ToolEngineTelemetry.ServiceVersion))
    .WithTracing(t => t
        .AddSource(ToolEngineTelemetry.ServiceName)       // custom tool spans
        .AddAspNetCoreInstrumentation()                   // HTTP spans
        .AddHttpClientInstrumentation()                   // outbound (webhooks)
        .AddEntityFrameworkCoreInstrumentation()          // DB query spans
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(m => m
        .AddMeter(ToolEngineTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
```

Configure via `"Otlp": { "Endpoint": "http://otel-collector:4317" }` in appsettings.

### G2 — Custom metrics (6 instruments)

All registered on `Meter("ToolEngine")` and exported via OTLP:

```csharp
public static readonly Histogram<long>     InvocationDuration   =
    Meter.CreateHistogram<long>("tool.invocation.duration", "ms",
        "Duration of tool invocations");
public static readonly Counter<long>       InvocationCount      =
    Meter.CreateCounter<long>("tool.invocation.count");
public static readonly UpDownCounter<long> PendingApprovals     =
    Meter.CreateUpDownCounter<long>("tool.approval.pending.count");
public static readonly Histogram<long>     ApprovalWaitDuration =
    Meter.CreateHistogram<long>("tool.approval.wait.duration", "ms");
public static readonly Counter<long>       LoopDetections       =
    Meter.CreateCounter<long>("tool.loop.detection.triggers");
public static readonly Counter<long>       DailyBudgetExceeded  =
    Meter.CreateCounter<long>("tool.daily.budget.exceeded");
```

Tags applied to all metrics: `tool` (full name), `tenant`, `status`.

### G3 — W3C TraceContext propagation

W3C `traceparent` / `tracestate` headers are propagated automatically by
`AddAspNetCoreInstrumentation()`. No manual wiring is needed.

**Semantic distinction to enforce:**
- `CorrelationId` (Guid) = **business concept** — one agent turn, one workflow instance
- `TraceId` (OTel) = **technical concept** — one HTTP request, one distributed trace span

Do NOT conflate them. Propagate `CorrelationId` as OTel baggage for cross-service tracing:
```csharp
activity?.SetBaggage("business.correlation_id", command.CorrelationId.ToString());
```

### G4 — Serilog PII masking (email addresses)

**File:** `src/Hosts/ToolEngine.Api/Program.cs`

```csharp
.Destructure.ByTransforming<string>(s => {
    var at = s.IndexOf('@');
    if (at < 0) return s;  // fast path — no '@' at all

    // ONLY mask strings that look like standalone email addresses.
    // Without whitespace + dot-after-@ checks, this would mask:
    //   npm scopes: @scope/pkg
    //   Slack mentions: @everyone
    //   File paths: /home/@user/file
    if (s.Contains(' ') || s.Contains('\t') || s.Contains('\n')) return s;
    if (!s.AsSpan(at + 1).Contains('.')) return s;

    var local  = s[..at];
    var prefix = local.Length >= 2 ? local[..2] : local[..1];
    return $"{prefix}***@***.***";
})
```

---

## Phase H — Compliance

### H1 — Append-only audit log (SOC 2 CC7)

**Entity:** `ToolInvocationEvent` (Phase 1) — NEVER update; only `Create()`.

**AuditBehavior emits one event per lifecycle transition:**

| Trigger | EventType |
|---|---|
| Record created (before handler) | `"Invoked"` |
| Handler returns success | `"Succeeded"` |
| Handler returns failure | `"Failed"` |
| Response is Suspended | `"Suspended"` |

```csharp
private async Task EmitEventAsync(
    ToolInvocationRecord record, string eventType, long? durationMs, CancellationToken ct)
{
    var evt = ToolInvocationEvent.Create(
        record.Id, eventType,
        record.CallerType,             // H4: on every event row
        record.GovernanceMetadataJson, // H5: on every event row
        durationMs, _clock);
    await _db.Set<ToolInvocationEvent>().AddAsync(evt, ct);
    // Do NOT call SaveChangesAsync here — caller batches all events
}
```

**Deployment runbook — restrict DB permissions (PostgreSQL example):**
```sql
GRANT INSERT ON "ToolInvocationEvents" TO toolengine_app;
REVOKE UPDATE, DELETE ON "ToolInvocationEvents" FROM toolengine_app;
```

`ToolInvocationRecord` is the operational convenience table (mutable — status updates).
`ToolInvocationEvent` is the SOC 2 evidence table (immutable — INSERT only).

### H2 — GDPR anonymisation + retention policy

**Entity fields (set in `ToolInvocationRecord.Create()`):**
```csharp
RetainUntil  = clock.UtcNow.AddDays(90);  // default 90-day retention window
IsAnonymized = false;
```

**Index for O(log n) sweep (in `AppDbContext.OnModelCreating`):**
```csharp
mb.Entity<ToolInvocationRecord>()
  .HasIndex(r => new { r.RetainUntil, r.IsAnonymized });
```

**Anonymize() method:**
```csharp
public void Anonymize()
{
    if (IsAnonymized) return;  // idempotent — safe to call multiple times
    UserId                = "[anonymized]";
    ErrorMessage          = null;    // may contain input details
    GovernanceMetadataJson = null;   // may contain model/session PII (H5)
    IsAnonymized          = true;
}
```

**Retention sweep query (background job):**
```csharp
var batch = await _db.Set<ToolInvocationRecord>()
    .Where(r => r.RetainUntil <= DateTimeOffset.UtcNow && !r.IsAnonymized)
    .Take(500)
    .ToListAsync(ct);
foreach (var r in batch) r.Anonymize();
await _db.SaveChangesAsync(ct);
```

**GDPR Recital 26 note:** `ToolInvocationEvent` retains `UserId` for legal accountability.
Event rows are NOT anonymised — `UserId` on events is required for SOC 2 CC7 attribution.
Only `ToolInvocationRecord` is anonymised.

### H3 — EU AI Act Article 14 acknowledgement

**Reference:** EU AI Act Article 14 §4 — operators of high-risk AI systems must ensure
that natural persons can effectively oversee the system, and that they are informed of
the risk level before granting approval.

**File:** `src/Infrastructure/ToolEngine.Infrastructure/Approval/AsyncApprovalGate.cs`

```csharp
// Generate acknowledgement only for High and Critical risk — not Medium or Low
if (context.Risk >= ApprovalRisk.High)
{
    var ack = new AcknowledgementStatement(
        RegBasis: "EU AI Act Article 14 §4",
        RiskLevel: context.Risk.ToString(),
        ToolFullName: context.ToolFullName,
        OperatorStatement:
            $"The approver acknowledges this is a {context.Risk}-risk AI-assisted action " +
            $"and accepts responsibility for its execution.",
        IssuedAt: _clock.UtcNow);
    // SetAcknowledgement uses ??= — immutable once set
    pending.SetAcknowledgement(JsonSerializer.Serialize(ack));
}
```

**AcknowledgementStatement record** (defined in Phase 1 `Core.Domain.Contracts`):
```csharp
public sealed record AcknowledgementStatement(
    string         RegBasis,           // "EU AI Act Article 14 §4"
    string         RiskLevel,          // "High" | "Critical"
    string         ToolFullName,
    string         OperatorStatement,
    DateTimeOffset IssuedAt);
```

The JSON blob is persisted to `PendingApproval.AcknowledgementJson` before the notification
is dispatched. This constitutes verifiable evidence that the approver was informed of the
risk classification before granting approval.

### H4 — CallerType / AI agent identity

**Reference:** NIST AI Agent Identity & Authorization Concept Paper (Feb 2026) —
machine identity must be non-delegatable and enforced at the execution boundary.

**Claim mapping at API boundary (`ToolEndpoints.cs`):**
```csharp
var callerType = ctx.User.FindFirst("caller_type")?.Value switch {
    "AiAgent"       => CallerType.AiAgent,
    "SystemService" => CallerType.SystemService,
    _               => CallerType.Human  // safe default — never assume AiAgent
};
```

**TenantClaimsTransformer:** If JWT has no `caller_type` claim, inject `Human` as default.

**Propagation chain:**
```
JWT claim "caller_type"
  → ExecuteToolCommand.CallerType
  → ToolInvocationRecord.CallerType         (record row)
  → ToolInvocationEvent.CallerType          (every event row)
```

**AgentOrchestrator (Phase L) — non-delegatable:**
```csharp
// CallerType = AiAgent is set unconditionally — not a parameter exposed to API callers
var command = new ExecuteToolCommand(..., CallerType: CallerType.AiAgent, ...);
```

### H5 — ISO 42001 GovernanceMetadataJson

**Reference:** ISO 42001 (AI Management Systems, 2024) — traceability of AI decision context.

**HTTP header at API boundary:**
```http
X-Governance-Metadata: {"model":"claude-opus-4-5","provider":"anthropic","sessionId":"abc123"}
```

**Propagation chain:**
```
X-Governance-Metadata header
  → ExecuteToolCommand.GovernanceMetadataJson
  → ToolInvocationRecord.GovernanceMetadataJson
  → ToolInvocationEvent.GovernanceMetadataJson (every event row)
```

**Set by AgentOrchestrator (Phase L) for LLM-initiated calls:**
```csharp
GovernanceMetadataJson = JsonSerializer.Serialize(new {
    provider  = _provider.Name,
    model     = _provider.ModelId,
    sessionId = session.Id
})
```

**Anonymize() clears GovernanceMetadataJson** (may contain session/user data — H2 + H5):
```csharp
GovernanceMetadataJson = null;  // done inside Anonymize()
```

---

## Configuration reference additions

**appsettings.json additions for this phase:**

```json
{
  "Database":  { "Provider": "sqlite" },
  "Cache":     { "Provider": "memory" },
  "Approval": {
    "BaseUrl":              "http://localhost:5174",
    "OtpMaxFailedAttempts": 5,
    "TokenExpiryMinutes":   60
  },
  "LoopDetection": { "MaxCallsPerCorrelation": 10 },
  "Otlp":      { "Endpoint": "" }
}
```

**Production overrides:**
```json
{
  "Database":  { "Provider": "postgresql" },
  "Cache":     { "Provider": "redis" },
  "Approval":  { "BaseUrl": "https://your-api.domain.com" },
  "ConnectionStrings": {
    "Default": "Host=db;Database=toolengine;Username=app;Password=...",
    "Redis":   "redis-host:6379"
  }
}
```

---

## Phase 5 completion checklist

### Phase E
- [ ] `PendingApproval.ApprovalToken` uses `RandomNumberGenerator.GetBytes(32)` — 64-char hex (E1)
- [ ] `FailedOtpAttempts` counter incremented; approval `Expired` at max 5 failures (E2)
- [ ] OTP endpoint requires rate-limit policy "otp-verify": 10/IP/10 min (E3)
- [ ] OTP hashed with PBKDF2-HMAC-SHA256 + 16-byte random salt (E3)
- [ ] OTP verified with `CryptographicOperations.FixedTimeEquals` (not `==`) (E3)
- [ ] `TenantAuthorizationBehavior` registered FIRST in DI (outermost) (E4)
- [ ] `DailyBudgetBehavior` uses UTC midnight boundary, returns 429 on exceeded (E5)
- [ ] 202 response includes `Location` header + `Retry-After: 10` (E6)
- [ ] `Jwt:Secret` validated ≥ 32 bytes at startup (E7)
- [ ] `Approval:BaseUrl` validated for HTTPS in non-development environments (E7)

### Phase F
- [ ] DB provider switchable via `"Database:Provider"` config key (F1)
- [ ] Production startup uses `MigrateAsync()`, development uses `EnsureDeletedAsync + EnsureCreatedAsync` (F2)
- [ ] `ICacheProvider.IncrementAsync` method implemented on both Memory and Distributed providers (F3)
- [ ] `LoopDetectionBehavior` uses `ICacheProvider.IncrementAsync` with TTL (not static dictionary) (F4)
- [ ] `CachedTenantReadRepository` registered as `Scoped` (F5)
- [ ] `Tenant.IsNamespaceAllowed()`: `Count == 0` returns `false` (deny all), `"*"` returns `true` (F6)
- [ ] `AsyncApprovalGate` writes `PendingApproval` + `OutboxMessage` in one `SaveChangesAsync` (F7)
- [ ] `NotificationDispatchService` polls every 15s, exponential backoff, abandons at 5 failures (F7)
- [ ] Idempotency check in `AsyncApprovalGate` before creating new `PendingApproval` (F8)
- [ ] `PagedListAsync` + `PagedResult<T>` on `IReadRepository` (F9)

### Phase G
- [ ] `ActivitySource("ToolEngine")` started in `AuditBehavior` with `tool.full_name`, `tenant.id`, `caller.type` tags (G1)
- [ ] All 6 metric instruments defined and recorded in correct behaviors (G2)
- [ ] OTel SDK propagates W3C `traceparent`/`tracestate` automatically via `AddAspNetCoreInstrumentation` (G3)
- [ ] `CorrelationId` and `TraceId` treated as distinct concepts — CorrelationId added as OTel baggage (G3)
- [ ] Serilog PII masking: whitespace AND dot-after-@ checks applied (NOT plain `s.Contains('@')`) (G4)

### Phase H
- [ ] `ToolInvocationEvent.Create()` is the ONLY way to create events — no update methods (H1)
- [ ] `AuditBehavior` emits `"Invoked"` before handler, `"Succeeded"/"Failed"/"Suspended"` after (H1)
- [ ] DB permissions: `ToolInvocationEvents` table grants INSERT only, not UPDATE/DELETE (H1 — runbook)
- [ ] `ToolInvocationRecord.RetainUntil = InvokedAt + 90 days` (H2)
- [ ] `Anonymize()` nulls `UserId`, `ErrorMessage`, `GovernanceMetadataJson`, sets `IsAnonymized = true` (H2)
- [ ] `Anonymize()` is idempotent — second call is a no-op (H2)
- [ ] `(RetainUntil, IsAnonymized)` composite index in `OnModelCreating` (H2)
- [ ] `AcknowledgementStatement` generated for `Risk.High` and `Risk.Critical` only (not Medium/Low) (H3)
- [ ] `AcknowledgementStatement` fields: `RegBasis`, `RiskLevel`, `ToolFullName`, `OperatorStatement`, `IssuedAt` (H3)
- [ ] `CallerType` defaults to `Human` at API boundary — never infer AiAgent from absent claim (H4)
- [ ] `CallerType` on both `ToolInvocationRecord` AND every `ToolInvocationEvent` row (H4)
- [ ] `GovernanceMetadataJson` on both record and every event row (H5)
- [ ] `Anonymize()` also clears `GovernanceMetadataJson` (H2 + H5)

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
