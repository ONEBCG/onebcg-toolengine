---
name: toolengine-advance-phase1-security-resilience
description: >
  Hardens the security and resilience posture of ToolEngine v2026.
  Covers: RS256/ES256 asymmetric JWT with key rotation, OAuth 2.1 + PKCE
  authorization flow, PostgreSQL Row-Level Security for tenant isolation,
  Polly v8 resilience pipelines (circuit breaker, retry with jitter, bulkhead,
  timeout), cryptographic SHA-256 hash-chained audit trail, mTLS for
  service-to-service communication, ISecretVault rotation hooks, and
  automatic re-execution of tool invocations after approval decision.
classification: Confidential - Internal Use Only
---

# Advancement Phase 1 — Security Hardening & Resilience

## Prerequisites

Phases 1–5 (core, tools, application, hosts, security-compliance) complete.
Zero-warning build on all projects.
PostgreSQL configured as the production database provider.

---

## Overview

| Item | Description | Standard |
|------|-------------|----------|
| A1.1 | Asymmetric JWT (RS256 / ES256) | RFC 7517, OWASP Auth Cheat Sheet |
| A1.2 | OAuth 2.1 + PKCE authorization flow | RFC 9700, RFC 7636 |
| A1.3 | PostgreSQL Row-Level Security (RLS) | AWS SaaS Lens, NIST SP 800-204 |
| A1.4 | Polly v8 resilience pipelines | Microsoft Resilience Guidance |
| A1.5 | Cryptographic audit chain (SHA-256 hash linking) | SOC 2 CC7, NIST SP 800-92 |
| A1.6 | mTLS for service-to-service | NIST Zero Trust SP 800-207 |
| A1.7 | ISecretVault rotation hooks | Azure Key Vault best practices |
| A1.8 | Auto re-execution after approval | Azure Async Request-Reply pattern |

---

## A1.1 — Asymmetric JWT (RS256 / ES256)

### Why

HMAC-SHA256 (symmetric) requires every service that verifies tokens to share
the same secret. A compromised microservice exposes the signing key. With RS256,
only the Authorization Server holds the private key; all other services use the
public key to verify — they cannot forge tokens.

### NuGet additions — `ToolEngine.Api`

```xml
<PackageReference Include="Microsoft.IdentityModel.Tokens" Version="7.*" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="7.*" />
```

### Key generation (CI/CD or Azure Key Vault)

```bash
# Generate RSA-2048 key pair (production minimum; RS512 uses 4096)
openssl genrsa -out toolengine-private.pem 2048
openssl rsa -in toolengine-private.pem -pubout -out toolengine-public.pem

# Or ES256 (ECDSA P-256 — faster, smaller tokens)
openssl ecparam -name prime256v1 -genkey -noout -out toolengine-ec-private.pem
openssl ec -in toolengine-ec-private.pem -pubout -out toolengine-ec-public.pem
```

Store private key in Azure Key Vault or AWS Secrets Manager. Never commit to git.

### Configuration — `appsettings.json`

```json
{
  "Jwt": {
    "Algorithm":        "RS256",
    "PublicKeyPath":    "/run/secrets/jwt-public.pem",
    "PrivateKeyPath":   "/run/secrets/jwt-private.pem",
    "Issuer":           "https://auth.onebcg.com",
    "Audience":         "toolengine-api",
    "ExpiryMinutes":    60,
    "ClockSkewSeconds": 30
  }
}
```

### `JwtKeyProvider.cs` — `ToolEngine.Infrastructure/Auth/`

```csharp
namespace ToolEngine.Infrastructure.Auth;

public interface IJwtKeyProvider
{
    RsaSecurityKey GetSigningKey();
    RsaSecurityKey GetValidationKey();
    Task RotateAsync(CancellationToken ct = default);
}

public sealed class FileJwtKeyProvider : IJwtKeyProvider
{
    private readonly JwtOptions _opts;
    private RsaSecurityKey? _signingKey;
    private RsaSecurityKey? _validationKey;

    public FileJwtKeyProvider(IOptions<JwtOptions> opts) => _opts = opts.Value;

    public RsaSecurityKey GetSigningKey()
    {
        if (_signingKey is not null) return _signingKey;
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(_opts.PrivateKeyPath));
        return _signingKey = new RsaSecurityKey(rsa);
    }

    public RsaSecurityKey GetValidationKey()
    {
        if (_validationKey is not null) return _validationKey;
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(_opts.PublicKeyPath));
        return _validationKey = new RsaSecurityKey(rsa);
    }

    // Called on Key Vault rotation event — invalidate cached keys
    public Task RotateAsync(CancellationToken ct = default)
    {
        _signingKey    = null;
        _validationKey = null;
        return Task.CompletedTask;
    }
}
```

### `Program.cs` — JWT validation switch

```csharp
// Replace the existing AddAuthentication block:
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var keyProvider = builder.Services.BuildServiceProvider()
                                .GetRequiredService<IJwtKeyProvider>();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = keyProvider.GetValidationKey(),
            ValidIssuer              = jwt.Issuer,
            ValidAudience            = jwt.Audience,
            ClockSkew                = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
            ValidAlgorithms          = new[] { SecurityAlgorithms.RsaSha256 }
        };
    });
```

---

## A1.2 — OAuth 2.1 + PKCE

### Why

OAuth 2.1 deprecates the implicit flow and mandates PKCE for all public clients.
This enables human operator SSO via existing IdPs (Azure AD, Okta) without
embedding long-lived secrets in the React frontend.

### Flow overview

```
Frontend (React SPA)              Authorization Server       ToolEngine API
      |                                    |                        |
      |-- 1. Generate code_verifier ------>|                        |
      |-- 2. GET /authorize                |                        |
      |       ?code_challenge=SHA256(cv)   |                        |
      |       &response_type=code -------->|                        |
      |<-- 3. Redirect with ?code=... -----|                        |
      |-- 4. POST /token                   |                        |
      |       code + code_verifier ------->|                        |
      |<-- 5. { access_token, id_token } --|                        |
      |-- 6. Bearer access_token ----------------------------------------->|
```

### NuGet additions — `ToolEngine.Api`

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="8.*" />
```

### PKCE helper — `ToolEngine.Api/Auth/PkceHelper.cs`

```csharp
namespace ToolEngine.Api.Auth;

public static class PkceHelper
{
    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncoder.Encode(bytes);
    }

    public static string GenerateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncoder.Encode(hash);
    }
}
```

### `Program.cs` — add OIDC alongside existing JWT Bearer

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme          = "smart";
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddPolicyScheme("smart", "JWT or OIDC", opts =>
{
    // API calls with Bearer token → JWT handler
    // Browser-initiated flows → OIDC handler
    opts.ForwardDefaultSelector = ctx =>
        ctx.Request.Headers.ContainsKey("Authorization")
            ? JwtBearerDefaults.AuthenticationScheme
            : OpenIdConnectDefaults.AuthenticationScheme;
})
.AddJwtBearer(/* existing RS256 config */)
.AddOpenIdConnect(oidcOpts =>
{
    oidcOpts.Authority            = config["Oidc:Authority"];   // Azure AD tenant URL
    oidcOpts.ClientId             = config["Oidc:ClientId"];
    oidcOpts.ResponseType         = "code";
    oidcOpts.UsePkce              = true;   // OAuth 2.1 mandatory
    oidcOpts.Scope.Add("openid");
    oidcOpts.Scope.Add("profile");
    oidcOpts.SaveTokens           = true;
});
```

### Configuration additions — `appsettings.json`

```json
{
  "Oidc": {
    "Authority": "https://login.microsoftonline.com/{tenant-id}/v2.0",
    "ClientId":  "your-app-client-id"
  }
}
```

---

## A1.3 — PostgreSQL Row-Level Security (RLS)

### Why

Application-layer tenant filtering (`WHERE TenantId = @id`) is the only boundary
today. A bug in a LINQ query or a raw SQL escape could expose cross-tenant data.
PostgreSQL RLS enforces the boundary at the database engine level — even if the
application sends a query without a `WHERE TenantId`, the DB returns zero rows.

### Migration — add RLS policies

```sql
-- Enable RLS on all tenant-scoped tables
ALTER TABLE "ToolInvocationRecords"  ENABLE ROW LEVEL SECURITY;
ALTER TABLE "ToolInvocationEvents"   ENABLE ROW LEVEL SECURITY;
ALTER TABLE "PendingApprovals"        ENABLE ROW LEVEL SECURITY;
ALTER TABLE "OutboxMessages"          ENABLE ROW LEVEL SECURITY;

-- Create a DB-level current_tenant function driven by a session variable
CREATE OR REPLACE FUNCTION current_tenant_id() RETURNS TEXT AS $$
  SELECT current_setting('app.current_tenant', TRUE);
$$ LANGUAGE SQL STABLE;

-- RLS policies: each role sees only its tenant's rows
CREATE POLICY tenant_isolation ON "ToolInvocationRecords"
  USING ("TenantId" = current_tenant_id());

CREATE POLICY tenant_isolation ON "ToolInvocationEvents"
  USING ("TenantId" = (
    SELECT "TenantId" FROM "ToolInvocationRecords"
    WHERE "Id" = "InvocationRecordId"
  ));

CREATE POLICY tenant_isolation ON "PendingApprovals"
  USING ("TenantId" = current_tenant_id());

-- ToolInvocationEvents: INSERT-only policy for app user (H1 compliance)
CREATE POLICY append_only_insert ON "ToolInvocationEvents"
  FOR INSERT WITH CHECK (TRUE);

-- Admin bypass (migration runner, DBA)
CREATE POLICY admin_bypass ON "ToolInvocationRecords"
  TO toolengine_admin USING (TRUE);
```

### Set tenant context in EF Core interceptor

```csharp
// ToolEngine.Infrastructure/Persistence/TenantRlsInterceptor.cs
public sealed class TenantRlsInterceptor : DbCommandInterceptor
{
    private readonly ICurrentUser _currentUser;

    public TenantRlsInterceptor(ICurrentUser currentUser) =>
        _currentUser = currentUser;

    public override async ValueTask<InterceptionResult<DbDataReader>>
        ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default)
    {
        if (_currentUser.TenantId is not null)
        {
            // Set the session-level variable before each query
            var setCmd = command.Connection!.CreateCommand();
            setCmd.CommandText =
                $"SET LOCAL app.current_tenant = '{_currentUser.TenantId}'";
            await setCmd.ExecuteNonQueryAsync(ct);
        }
        return result;
    }
}
```

### Register in `AppDbContext`

```csharp
// In AddToolInfrastructure extension, after UseNpgsql:
options.AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
```

---

## A1.4 — Polly v8 Resilience Pipelines

### Why

LLM provider calls, approval webhook deliveries, and external API tool calls
have no fault isolation today. One slow LLM response can exhaust the thread
pool. Polly v8 (the .NET 8 native resilience API) provides composable
pipelines: timeout → retry → circuit breaker → bulkhead.

### NuGet additions — `ToolEngine.Infrastructure`, `ToolEngine.Llm`

```xml
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.*" />
<PackageReference Include="Polly.Core" Version="8.*" />
```

### Resilience pipeline definitions — `ToolEngine.Infrastructure/Resilience/ResiliencePipelines.cs`

```csharp
namespace ToolEngine.Infrastructure.Resilience;

public static class ResiliencePipelines
{
    // For LLM provider HTTP clients — tolerant of transient failures + rate limits
    public static ResiliencePipelineBuilder<HttpResponseMessage> LlmProvider(
        ResiliencePipelineBuilder<HttpResponseMessage> builder) =>
        builder
            .AddTimeout(TimeSpan.FromSeconds(30))
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.FromSeconds(2),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,                         // prevents thundering herd
                ShouldHandle     = args => ValueTask.FromResult(
                    args.Outcome.Result?.StatusCode is
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout)
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio          = 0.5,       // open when 50% of calls fail
                SamplingDuration      = TimeSpan.FromSeconds(30),
                MinimumThroughput     = 5,
                BreakDuration         = TimeSpan.FromSeconds(60),
                OnOpened              = args =>
                {
                    ToolEngineTelemetry.CircuitBreakerOpened.Add(1);
                    return ValueTask.CompletedTask;
                }
            });

    // For approval webhook delivery — conservative retry, no circuit breaker
    public static ResiliencePipelineBuilder<HttpResponseMessage> WebhookDelivery(
        ResiliencePipelineBuilder<HttpResponseMessage> builder) =>
        builder
            .AddTimeout(TimeSpan.FromSeconds(10))
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay            = TimeSpan.FromSeconds(30),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true
            });

    // Bulkhead for tool executor — cap concurrent tool executions per tenant
    public static ResiliencePipelineBuilder BulkheadToolExecution(
        ResiliencePipelineBuilder builder) =>
        builder
            .AddConcurrencyLimiter(new ConcurrencyLimiterStrategyOptions
            {
                PermittedConcurrencyLimiters = 20,   // max concurrent tool executions
                QueuedTasksLimiters          = 50    // queue depth before rejecting
            });
}
```

### Wire up in `Program.cs`

```csharp
// LLM provider HttpClient with resilience
builder.Services.AddHttpClient<ILlmProvider, AnthropicLlmProvider>()
    .AddResilienceHandler("llm-provider", builder =>
        ResiliencePipelines.LlmProvider(builder));

// Webhook delivery HttpClient
builder.Services.AddHttpClient<IApprovalChannel, WebhookApprovalChannel>()
    .AddResilienceHandler("webhook-delivery", builder =>
        ResiliencePipelines.WebhookDelivery(builder));
```

---

## A1.5 — Cryptographic Audit Chain (SHA-256 Hash Linking)

### Why

The current `ToolInvocationEvent` table is append-only by DB permission policy.
However, a compromised DBA or a SQL injection could silently delete rows.
Hash-chaining links each event to its predecessor — any tampering breaks the
chain and is detectable.

### Domain change — `ToolInvocationEvent.cs`

```csharp
// Add to ToolInvocationEvent entity:
public string? PreviousEventHash { get; private set; }   // SHA-256 of previous row
public string  EventHash         { get; private set; } = default!;  // SHA-256 of this row

// Compute hash over: Id + InvocationRecordId + EventType + OccurredAt + CallerType
public static string ComputeHash(ToolInvocationEvent evt, string? previousHash)
{
    var input = $"{evt.Id}|{evt.InvocationRecordId}|{evt.EventType}" +
                $"|{evt.OccurredAt:O}|{evt.CallerType}|{previousHash ?? "GENESIS"}";
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
```

### `AuditChainService.cs` — `ToolEngine.Infrastructure/Audit/`

```csharp
namespace ToolEngine.Infrastructure.Audit;

public sealed class AuditChainService
{
    private readonly AppDbContext _db;

    public AuditChainService(AppDbContext db) => _db = db;

    public async Task<ToolInvocationEvent> CreateLinkedEventAsync(
        Guid invocationRecordId, string eventType,
        CallerType callerType, string? governanceMetadataJson,
        long? durationMs, IDateTimeProvider clock,
        CancellationToken ct = default)
    {
        // Get the last event for this invocation to form the chain link
        var lastEvent = await _db.Set<ToolInvocationEvent>()
            .Where(e => e.InvocationRecordId == invocationRecordId)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync(ct);

        var evt = ToolInvocationEvent.Create(
            invocationRecordId, eventType,
            callerType, governanceMetadataJson,
            durationMs, clock);

        evt.SetChain(
            previousHash: lastEvent?.EventHash,
            hash: ToolInvocationEvent.ComputeHash(evt, lastEvent?.EventHash));

        return evt;
    }

    // Verify integrity of a chain — returns first broken link or null
    public async Task<Guid?> VerifyChainAsync(
        Guid invocationRecordId, CancellationToken ct = default)
    {
        var events = await _db.Set<ToolInvocationEvent>()
            .Where(e => e.InvocationRecordId == invocationRecordId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

        string? expectedPrevious = null;
        foreach (var evt in events)
        {
            var expected = ToolInvocationEvent.ComputeHash(evt, expectedPrevious);
            if (evt.EventHash != expected) return evt.Id;  // broken link found
            expectedPrevious = evt.EventHash;
        }
        return null;  // chain intact
    }
}
```

---

## A1.6 — mTLS for Service-to-Service

### Why

JWT bearer tokens secure the API → client boundary. Between internal services
(API pod → Notification worker, API → Agent orchestrator), mTLS provides
mutual authentication: both sides prove identity via certificate, preventing
lateral movement if a pod is compromised.

### Certificate provisioning (Kubernetes / cert-manager)

```yaml
# cert-manager Certificate for ToolEngine API
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: toolengine-api-mtls
  namespace: toolengine
spec:
  secretName: toolengine-api-tls
  duration: 2160h    # 90 days
  renewBefore: 720h  # Renew 30 days before expiry
  subject:
    organizations: ["ONE BCG"]
  commonName: toolengine-api.toolengine.svc.cluster.local
  dnsNames:
    - toolengine-api.toolengine.svc.cluster.local
  issuerRef:
    name: toolengine-internal-ca
    kind: ClusterIssuer
```

### `HttpClient` mTLS configuration — `Program.cs`

```csharp
// Internal service-to-service HTTP client with client certificate
builder.Services.AddHttpClient("internal-services")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var cert = X509Certificate2.CreateFromPemFile(
            "/var/run/secrets/tls/tls.crt",
            "/var/run/secrets/tls/tls.key");

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(cert);
        // Validate server cert against internal CA
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            // Replace with: cert pinning or internal CA validation in production
        return handler;
    });
```

---

## A1.7 — ISecretVault Rotation Hooks

### Why

Azure Key Vault fires an `EventGrid` event when a secret version changes.
Without a rotation hook, the running process continues to use the old secret
value until restart — creating a window where token verification may fail for
tokens issued against the new key.

### Updated `ISecretVault` — `ToolEngine.Core.Abstractions/Secrets/`

```csharp
public interface ISecretVault
{
    Task<string> GetSecretAsync(
        string scope, string name, string key, CancellationToken ct = default);

    // Called when Key Vault fires a rotation event
    Task OnSecretRotatedAsync(
        string secretName, CancellationToken ct = default);
}
```

### `KeyVaultSecretVault.cs` — `ToolEngine.Infrastructure/Secrets/`

```csharp
public sealed class KeyVaultSecretVault : ISecretVault
{
    private readonly SecretClient     _client;
    private readonly IJwtKeyProvider  _jwtKeyProvider;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public async Task<string> GetSecretAsync(
        string scope, string name, string key, CancellationToken ct = default)
    {
        var cacheKey = $"{scope}:{name}:{key}";
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        var secret = await _client.GetSecretAsync($"{scope}-{name}-{key}", ct: ct);
        return _cache[cacheKey] = secret.Value.Value;
    }

    // Fired by Event Grid webhook → POST /internal/secrets/rotated
    public async Task OnSecretRotatedAsync(string secretName, CancellationToken ct = default)
    {
        // Evict cached value — next GetSecretAsync call fetches fresh value
        foreach (var key in _cache.Keys.Where(k => k.Contains(secretName)))
            _cache.TryRemove(key, out _);

        // If it's the JWT signing key, rotate the in-memory RSA key
        if (secretName.Contains("jwt"))
            await _jwtKeyProvider.RotateAsync(ct);
    }
}
```

### Internal rotation webhook endpoint — `ToolEngine.Api/Endpoints/InternalEndpoints.cs`

```csharp
app.MapPost("/internal/secrets/rotated", async (
    [FromBody] SecretRotationEvent evt,
    ISecretVault vault, CancellationToken ct) =>
{
    await vault.OnSecretRotatedAsync(evt.SecretName, ct);
    return Results.Ok();
})
.RequireAuthorization("InternalOnly")  // only cluster-internal traffic
.ExcludeFromDescription();             // hide from Swagger
```

---

## A1.8 — Auto Re-Execution After Approval

### Why

Currently a client must resend the full tool invocation request after an
approval is granted. This is the most significant UX gap — it requires the
calling agent or UI to implement polling and re-invocation logic.
Auto re-execution stores the serialized input with the approval record
and replays it atomically when the approver decides.

### Domain change — `PendingApproval.cs`

```csharp
// Add serialized command to enable replay
public string? SerializedCommand { get; private set; }
public string? CommandType       { get; private set; }

public void StoreCommand(string commandType, string serializedCommand)
{
    CommandType       ??= commandType;       // immutable once set
    SerializedCommand ??= serializedCommand;
}
```

### Store command in `ApprovalBehavior.cs`

```csharp
// After creating PendingApproval, store the command for replay
var commandJson = JsonSerializer.Serialize(request, request.GetType());
pending.StoreCommand(request.GetType().AssemblyQualifiedName!, commandJson);
await _approvalGate.RequestApprovalAsync(pending, ct);
```

### `ApprovalReExecutionService.cs` — `ToolEngine.Infrastructure/Approval/`

```csharp
namespace ToolEngine.Infrastructure.Approval;

public sealed class ApprovalReExecutionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApprovalReExecutionService> _logger;

    public async Task ReExecuteAsync(Guid approvalId, CancellationToken ct = default)
    {
        await using var scope   = _scopeFactory.CreateAsyncScope();
        var db       = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var approval = await db.Set<PendingApproval>()
            .FirstOrDefaultAsync(a => a.Id == approvalId, ct);

        if (approval?.SerializedCommand is null || approval.CommandType is null)
        {
            _logger.LogWarning("ApprovalId {Id}: no stored command — skipping re-execution", approvalId);
            return;
        }

        var commandType = Type.GetType(approval.CommandType);
        if (commandType is null)
        {
            _logger.LogError("ApprovalId {Id}: command type {Type} not found", approvalId, approval.CommandType);
            return;
        }

        var command = JsonSerializer.Deserialize(approval.SerializedCommand, commandType);
        if (command is null) return;

        // Dispatch through the full MediatR pipeline — audit, budget, loop detection all fire
        await mediator.Send(command, ct);
        _logger.LogInformation("ApprovalId {Id}: re-execution dispatched", approvalId);
    }
}
```

### Trigger re-execution in `ApprovalEndpoints.cs`

```csharp
// After DecideAsync sets status to Approved:
if (decision == ApprovalAction.Approve)
{
    var reExec = ctx.RequestServices
        .GetRequiredService<ApprovalReExecutionService>();
    _ = reExec.ReExecuteAsync(approvalId, CancellationToken.None);
    // Fire-and-forget — client polls /invocations/{id}/status for result
}
```

---

## Phase A1 Completion Checklist

### A1.1 — Asymmetric JWT
- [ ] `IJwtKeyProvider` interface with `GetSigningKey`, `GetValidationKey`, `RotateAsync`
- [ ] `FileJwtKeyProvider` reads PEM files; `RotateAsync` nulls cached keys
- [ ] JWT validation uses `ValidAlgorithms = ["RS256"]` — rejects symmetric tokens
- [ ] `ClockSkew` set to ≤ 30 seconds (not the default 5 minutes)
- [ ] Startup: fail if key files are missing or unreadable

### A1.2 — OAuth 2.1 + PKCE
- [ ] `PkceHelper` uses `SHA256.HashData` + `Base64UrlEncoder` (not plain Base64)
- [ ] `UsePkce = true` on OIDC options
- [ ] Policy scheme routes Bearer → JWT, browser → OIDC
- [ ] No implicit flow (`response_type=token` not accepted)

### A1.3 — PostgreSQL RLS
- [ ] RLS enabled on all 4 tenant-scoped tables
- [ ] `current_tenant_id()` reads `app.current_tenant` session variable
- [ ] `TenantRlsInterceptor` sets `SET LOCAL app.current_tenant` before every query
- [ ] Admin bypass policy created for migration runner role
- [ ] `ToolInvocationEvents`: append-only (`INSERT WITH CHECK`) RLS policy

### A1.4 — Polly v8
- [ ] LLM provider pipeline: timeout (30s) → retry (3× jitter) → circuit breaker (50% / 30s)
- [ ] Webhook pipeline: timeout (10s) → retry (5× exponential)
- [ ] Bulkhead: 20 concurrent tool executions, 50 queued before rejection
- [ ] `CircuitBreakerOpened` metric incremented on circuit open
- [ ] All HttpClients using `.AddResilienceHandler()`

### A1.5 — Cryptographic Audit Chain
- [ ] `ToolInvocationEvent` has `PreviousEventHash` and `EventHash` columns
- [ ] `ComputeHash` covers: Id, InvocationRecordId, EventType, OccurredAt, CallerType, PreviousHash
- [ ] `AuditChainService.VerifyChainAsync` returns first broken link Guid or null
- [ ] `AuditBehavior` uses `AuditChainService` instead of direct `ToolInvocationEvent.Create`

### A1.6 — mTLS
- [ ] cert-manager Certificate resource for API and worker pods
- [ ] Internal `HttpClient` loads certificate from `/var/run/secrets/tls/`
- [ ] Internal endpoint `/internal/secrets/rotated` requires `InternalOnly` policy
- [ ] Server certificate validated against internal CA (not `DangerousAcceptAny` in production)

### A1.7 — Secret Rotation
- [ ] `ISecretVault.OnSecretRotatedAsync` method on interface
- [ ] `KeyVaultSecretVault` evicts cache on rotation
- [ ] JWT key rotation triggered when secret name contains `"jwt"`
- [ ] Rotation webhook endpoint excluded from Swagger / public docs

### A1.8 — Auto Re-Execution
- [ ] `PendingApproval.StoreCommand` is idempotent (`??=` pattern)
- [ ] `ApprovalBehavior` serializes command before suspending
- [ ] `ApprovalReExecutionService` resolves command type via `Type.GetType`
- [ ] Re-execution dispatched through full MediatR pipeline (audit fires again)
- [ ] Re-execution is fire-and-forget — client polls invocation status endpoint

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
