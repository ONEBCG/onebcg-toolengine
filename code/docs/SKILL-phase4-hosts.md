---
name: toolengine-phase4-hosts
description: >
  Scaffolds Phase 4 of ToolEngine v2026: ToolEngine.Api (ASP.NET Core Minimal API
  with JWT auth, rate limiting, OTel, Swagger, modular DB provider, dev tenant seed,
  HTTPS validation), ToolEngine.Cli (Spectre.Console REPL with ask/chat/invoke and
  async-safe startup), and the React/TypeScript/Vite frontend developer console with
  ONE BCG brand tokens, flat ToolDescriptor contract, and res.ok error guard.
classification: Confidential - Internal Use Only
---

# Phase 4 — REST API Host + CLI REPL + Frontend

## Prerequisites

Phases 1–3 complete. `dotnet build` passes with zero warnings on all 8 projects.

---

## What this phase produces

```
src/
  Hosts/
    ToolEngine.Api/     ← ASP.NET Core Minimal API, JWT, rate limit, OTel, Swagger
    ToolEngine.Cli/     ← Spectre.Console interactive REPL
  frontend/             ← React 18 + TypeScript + Vite developer console
```

---

## API Host scaffold

```bash
dotnet new webapi -n ToolEngine.Api \
  -o src/Hosts/ToolEngine.Api --framework net8.0 --use-minimal-apis
dotnet sln add src/Hosts/ToolEngine.Api

dotnet add src/Hosts/ToolEngine.Api reference \
  src/Application/ToolEngine.Application \
  src/Infrastructure/ToolEngine.Infrastructure \
  src/Tools/ToolEngine.Tools.Registry \
  src/Tools/ToolEngine.Tools.Executor \
  src/Tools/ToolEngine.Tools.Samples \
  src/Llm/ToolEngine.Llm          # Phase L — add when Llm project exists
```

**API NuGet packages:**
```xml
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.*" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.*" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
```

---

## API Program.cs — complete startup sequence

### 1. Bootstrap logger

```csharp
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
try {
var builder = WebApplication.CreateBuilder(args);
```

### 2. Serilog with PII masking (G4)

```csharp
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // G4: mask email addresses in structured log properties
    // IMPORTANT: check for whitespace AND dot-after-@ to avoid masking
    // npm @scope/pkg, Slack @everyone, file paths, etc.
    .Destructure.ByTransforming<string>(s => {
        var at = s.IndexOf('@');
        if (at < 0) return s;
        if (s.Contains(' ') || s.Contains('\t') || s.Contains('\n')) return s;
        if (!s.AsSpan(at + 1).Contains('.')) return s;
        var local = s[..at];
        return $"{(local.Length >= 2 ? local[..2] : local[..1])}***@***.***";
    }));
```

### 3. JWT authentication + E7 key length validation

```csharp
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt settings missing.");

// E7: fail fast — API must not start with a short signing key
if (Encoding.UTF8.GetBytes(jwt.Secret).Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be at least 32 bytes (256 bits). " +
        "Generate: openssl rand -base64 32");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => opt.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidateAudience = true,
        ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer              = jwt.Issuer,
        ValidAudience            = jwt.Audience,
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret))
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(jwt);
builder.Services.AddTransient<IClaimsTransformation, TenantClaimsTransformer>();
```

### 4. Tool engine DI

```csharp
builder.Services.AddToolRegistry();
builder.Services.AddToolSamples();
builder.Services.AddToolExecutor();
builder.Services.AddToolApplication();
builder.Services.AddToolLlm(builder.Configuration);  // Phase L
```

### 5. Modular DB provider (F1)

```csharp
var dbOpts    = builder.Configuration.GetSection("Database").Get<DatabaseOptions>() ?? new();
var connStr   = builder.Configuration.GetConnectionString("Default") ?? "Data Source=toolengine-dev.db";
var cacheOpts = builder.Configuration.GetSection("Cache").Get<CacheOptions>() ?? new();

builder.Services.AddToolInfrastructure(opt => {
    switch (dbOpts.Provider.ToLowerInvariant()) {
        case "postgresql": opt.UseNpgsql(connStr); break;
        case "sqlserver":  opt.UseSqlServer(connStr); break;
        default:           opt.UseSqlite(connStr); break;  // sqlite = dev default
    }
});
```

### 6. Redis cache (F3 — optional, memory is fallback)

```csharp
if (cacheOpts.Provider.Equals("redis", StringComparison.OrdinalIgnoreCase)) {
    builder.Services.AddStackExchangeRedisCache(opt =>
        opt.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");
    builder.Services.AddDistributedCacheProvider();
}
// If not configured, AddToolInfrastructure registers MemoryCacheProvider as fallback
```

### 7. Rate limiting — OTP endpoint (E3)

```csharp
builder.Services.AddRateLimiter(opt => {
    opt.AddPolicy("otp-verify", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new SlidingWindowRateLimiterOptions {
                Window = TimeSpan.FromMinutes(10), SegmentsPerWindow = 5,
                PermitLimit = 10, QueueLimit = 0
            }));
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.OnRejected = async (ctx, token) => {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "TOO_MANY_REQUESTS",
                  description = "Too many OTP verification attempts. Try again in 10 minutes." }, token);
    };
});
```

### 8. OpenTelemetry (G1 / G2)

```csharp
var otlpEndpoint = builder.Configuration["Otlp:Endpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ToolEngineTelemetry.ServiceName, ToolEngineTelemetry.ServiceVersion))
    .WithTracing(t => {
        t.AddSource(ToolEngineTelemetry.ServiceName)
         .AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddEntityFrameworkCoreInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(m => {
        m.AddMeter(ToolEngineTelemetry.ServiceName).AddAspNetCoreInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
```

### 9. Build + startup security validation (E7)

```csharp
var app = builder.Build();

// E7: fail fast before accepting any traffic in non-development
if (!app.Environment.IsDevelopment()) {
    var approvalOpts = app.Services.GetRequiredService<IOptions<ApprovalOptions>>().Value;
    if (!approvalOpts.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "Approval:BaseUrl must use HTTPS in non-development environments. " +
            "Magic links sent over HTTP are vulnerable to interception.");
}
```

### 10. Database init — CRITICAL pattern

```csharp
{
    using var dbScope = app.Services.CreateScope();
    var db = dbScope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (app.Environment.IsDevelopment()) {
        // EnsureDeleted + EnsureCreated = always rebuild schema from current model.
        // Prevents stale-schema errors (e.g. "no such table: OutboxMessages") when
        // new entities are added between runs. Dev data is intentionally ephemeral.
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed onebcg-default-tenant — required by TenantAuthorizationBehavior.
        // Without this seed, every invocation returns 401 immediately after startup.
        var clock = dbScope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var devTenant = Tenant.Create("onebcg-default-tenant", "ONE BCG Default Tenant", "dev-seed", clock).Value;
        devTenant.AllowNamespace("*");  // F6: explicit wildcard required (deny-by-default)
        db.Set<Tenant>().Add(devTenant);
        await db.SaveChangesAsync();
    } else {
        await db.Database.MigrateAsync();  // production: apply pending EF migrations
    }
}
```

### 11. Middleware pipeline (ORDER MATTERS)

```csharp
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors();
}

app.UseSerilogRequestLogging();
app.UseRateLimiter();       // MUST be before authentication
app.UseAuthentication();
app.UseAuthorization();
```

### 12. Endpoint mapping

```csharp
app.MapToolEndpoints();        // POST /tools/{ns}/{name}/{version}/invoke, GET /tools
app.MapApprovalEndpoints();    // POST /approvals/{token}/decide, POST /approvals/otp/verify
app.MapInvocationEndpoints();  // GET /invocations/{id}/status
app.MapHealthEndpoints();      // GET /health
app.MapDevEndpoints();         // GET /dev/token — dev only
app.MapAgentEndpoints();       // POST /agent/chat, POST /agent/chat/stream — Phase L

app.Run();
} catch (Exception ex) { Log.Fatal(ex, "ToolEngine.Api terminated unexpectedly."); }
finally { Log.CloseAndFlush(); }
```

---

## Key endpoint implementations

### DevEndpoints.cs

```csharp
// Default tenant: "onebcg-default-tenant" (NOT "acme-corp" or any other value)
app.MapGet("/dev/token", ([FromQuery] string? tenant, JwtSettings jwt) => {
    var tenantId = (tenant ?? "onebcg-default-tenant").Trim().ToLowerInvariant();
    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var claims = new[] {
        new Claim(JwtRegisteredClaimNames.Sub, "dev-user"),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim("tenant_id", tenantId),
    };
    var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims,
        expires: DateTime.UtcNow.AddHours(8), signingCredentials: creds);
    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token),
                             expiresIn = 28800, tenantId });
}).AllowAnonymous();
```

### ToolEndpoints.cs — 202 with Location header (E6) + flat GET /tools

```csharp
// E6: 202 must include Location header pointing to poll URL
if (response.IsSuspended) {
    var pollUrl = $"/invocations/{response.PendingInvocationId}/status";
    ctx.Response.Headers["Retry-After"] = "10";
    return Results.Accepted(pollUrl, new {   // location param sets Location header
        status = "pending_approval",
        invocationId = response.PendingInvocationId,
        pollUrl,
        message = "Tool execution is suspended pending human approval."
    });
}

// GET /tools: FLAT ToolSummaryResponse (no nested metadata)
app.MapGet("/tools", (IToolRegistry registry, HttpContext ctx) => {
    var tenantId = ctx.User.FindFirst("tenant_id")?.Value ?? "onebcg-default-tenant";
    return Results.Ok(registry.ListForTenant(tenantId)
        .Select(d => new ToolSummaryResponse(
            d.FullName, d.Namespace, d.Name, d.Version, d.Schema.Description,
            (int)d.Type, d.IsEnabled, d.TenantId, d.Schema.InputSchema, d.Schema.OutputSchema)));
}).RequireAuthorization();
```

### TenantClaimsTransformer.cs (H4 — CallerType from JWT)

```csharp
public class TenantClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // CallerType: default to Human — never assume AiAgent from missing claim
        if (!principal.HasClaim(c => c.Type == "caller_type"))
        {
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim("caller_type", CallerType.Human.ToString()));
            principal.AddIdentity(identity);
        }
        return Task.FromResult(principal);
    }
}
```

---

## appsettings.json (API)

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Information", "Override": { "Microsoft": "Warning" } }
  },
  "Jwt": {
    "Secret": "REPLACE_WITH_SECURE_32_BYTE_SECRET_openssl_rand_base64_32",
    "Issuer": "toolengine-api",
    "Audience": "toolengine-clients"
  },
  "Database": { "Provider": "sqlite" },
  "Cache": { "Provider": "memory" },
  "ConnectionStrings": { "Default": "Data Source=toolengine-dev.db" },
  "Approval": { "BaseUrl": "http://localhost:5174", "OtpMaxFailedAttempts": 5, "TokenExpiryMinutes": 60 },
  "Otlp": { "Endpoint": "" },
  "Llm": {
    "DefaultProvider": "anthropic",
    "Routing": { "FallbackChain": ["anthropic", "openai"] },
    "Budget": { "MaxTokensPerRequest": 4096, "MaxTokensPerSession": 32768, "MaxIterations": 10 },
    "Providers": {
      "anthropic": { "Model": "claude-opus-4-5", "ApiKeyEnvVar": "ANTHROPIC_API_KEY", "MaxTokens": 4096 },
      "openai":    { "Model": "gpt-4o",          "ApiKeyEnvVar": "OPENAI_API_KEY",    "MaxTokens": 4096 },
      "ollama":    { "BaseUrl": "http://localhost:11434", "Model": "llama3.1:8b",      "MaxTokens": 4096 }
    }
  }
}
```

---

## CLI Host scaffold

```bash
dotnet new console -n ToolEngine.Cli -o src/Hosts/ToolEngine.Cli --framework net8.0
dotnet sln add src/Hosts/ToolEngine.Cli
dotnet add src/Hosts/ToolEngine.Cli reference \
  src/Application/ToolEngine.Application \
  src/Infrastructure/ToolEngine.Infrastructure \
  src/Tools/ToolEngine.Tools.Registry \
  src/Tools/ToolEngine.Tools.Executor \
  src/Tools/ToolEngine.Tools.Samples \
  src/Llm/ToolEngine.Llm          # Phase L
```

**CLI NuGet packages:**
```xml
<PackageReference Include="Spectre.Console" Version="0.49.*" />
<PackageReference Include="Serilog.Extensions.Hosting" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.*" />
```

---

## CLI Program.cs — CRITICAL startup pattern

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .MinimumLevel.Warning()
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .ConfigureServices((ctx, services) => {
        services.AddToolRegistry();
        services.AddToolSamples();
        services.AddToolExecutor();
        services.AddToolApplication();
        services.AddToolLlm(ctx.Configuration);  // Phase L
        services.AddToolInfrastructure(
            opt => opt.UseSqlite("Data Source=toolengine-cli.db"));
        // CLI uses synchronous console prompts — NOT AsyncApprovalGate
        services.AddTransient<IHumanApprovalGate, ConsoleApprovalGate>();
        services.AddTransient<ReplLoop>();
    })
    .Build();

// CRITICAL: CreateAsyncScope NOT CreateScope
// UnitOfWork implements IAsyncDisposable only — sync Dispose() throws
// EnsureDeleted + EnsureCreated: always rebuild schema to avoid stale state
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider
                  .GetRequiredService<AppDbContext>();
    await db.Database.EnsureDeletedAsync();   // drop stale schema
    await db.Database.EnsureCreatedAsync();   // rebuild from current model

    // Seed dev tenant — TenantAuthorizationBehavior requires this
    var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
    var devTenant = Tenant.Create("onebcg-default-tenant", "ONE BCG Default Tenant", "cli-seed", clock).Value;
    devTenant.AllowNamespace("*");
    db.Set<Tenant>().Add(devTenant);
    await db.SaveChangesAsync();
}

// StartAsync triggers IHostedService.StartAsync (tool registration)
await host.StartAsync();
var repl = host.Services.GetRequiredService<ReplLoop>();
await repl.RunAsync(CancellationToken.None);
await host.StopAsync();
```

### CLI REPL commands

```
list                                      — list all registered tools
invoke <ns> <name> <version> <json>      — direct invocation with JSON input
ask <text>                                — single-turn LLM tool selection + execution
chat                                      — enter multi-turn session
chat end                                  — end current session
exit / quit                               — exit the REPL
```

### ConsoleApprovalGate.cs

```csharp
public sealed class ConsoleApprovalGate : IHumanApprovalGate
{
    public Task<ApprovalDecision> RequestApprovalAsync(ApprovalContext context, CancellationToken ct)
    {
        var riskColour = context.Risk switch {
            ApprovalRisk.Critical => "red",
            ApprovalRisk.High     => "darkorange",
            _                     => "yellow"
        };
        AnsiConsole.MarkupLine($"[{riskColour}]⚠  Approval Required — {context.Risk} Risk[/]");
        AnsiConsole.MarkupLine($"  Tool   : [bold]{context.ToolFullName}[/]");
        AnsiConsole.MarkupLine($"  Reason : {context.Reason}");
        var allow = AnsiConsole.Confirm(
            $"Allow execution of '{context.ToolFullName}'?", defaultValue: false);
        return Task.FromResult(allow
            ? ApprovalDecision.Allowed()
            : ApprovalDecision.Denied("Operator denied via console."));
    }
}
```

### appsettings.json (CLI)

```json
{
  "Serilog": { "MinimumLevel": { "Default": "Warning" } },
  "Llm": {
    "DefaultProvider": "ollama",
    "Budget": { "MaxTokensPerRequest": 4096, "MaxTokensPerSession": 32768, "MaxIterations": 10 },
    "Providers": {
      "ollama": { "BaseUrl": "http://localhost:11434", "Model": "llama3.1:8b", "MaxTokens": 4096 }
    }
  }
}
```

---

## Frontend — React/TypeScript/Vite developer console

```bash
cd src
npm create vite@latest frontend -- --template react-ts
cd frontend
npm install
```

### File structure

```
src/frontend/
├── index.html
├── vite.config.ts          — proxy /api → http://localhost:5174
└── src/
    ├── api.ts              — fetch wrappers + exported BASE
    ├── types.ts            — ToolDescriptor FLAT interface
    ├── App.tsx             — layout: header, tab bar, main panel
    ├── App.css             — ONE BCG brand CSS variables
    └── components/
        ├── ToolList.tsx    — tool browser sidebar
        ├── ToolInvoker.tsx — JSON editor + Invoke button
        └── ResponsePanel.tsx — result display
```

### api.ts — exported BASE + res.ok guard

```typescript
// Export BASE so App.tsx can build the Swagger link without duplicating the URL
export const BASE = 'http://localhost:5174'

export async function fetchDevToken(): Promise<string> {
  const res = await fetch(`${BASE}/dev/token`)
  const data = await res.json()
  return data.token as string
}

export async function fetchTools(token: string): Promise<ToolDescriptor[]> {
  const res = await fetch(`${BASE}/tools`, {
    headers: { 'Authorization': `Bearer ${token}` }
  })
  return res.json()
}

// CRITICAL: check res.ok before parsing.
// fetch() does NOT throw on 4xx/5xx — only on network failure.
// Without this guard, ProblemDetails JSON is forwarded to ResponsePanel
// which crashes accessing metrics.duration on undefined.
export async function invokeTool(
  ns: string, name: string, version: string,
  input: unknown, token: string
): Promise<ToolInvocationResult> {
  const res = await fetch(`${BASE}/tools/${ns}/${name}/${version}/invoke`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`,
    },
    body: JSON.stringify(input),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({})) as { detail?: string; title?: string }
    throw new Error(err.detail ?? err.title ?? `Invocation failed: HTTP ${res.status}`)
  }
  return res.json()
}
```

### types.ts — FLAT ToolDescriptor

```typescript
// FLAT interface — must match ToolSummaryResponse from the backend exactly.
// No nested metadata sub-object. Contract drift causes the tools tab to render
// blank: tool.metadata.name returns undefined, buttons appear unclickable.
export interface ToolDescriptor {
  fullName:    string   // "math.calculate"
  namespace:   string   // "math"
  name:        string   // "calculate"
  version:     string   // "v1"
  description: string
  type:        number   // int — not enum
  isEnabled:   boolean
  tenantId:    string | null
  inputSchema: Record<string, unknown>
  outputSchema: Record<string, unknown>
}

export interface ToolInvocationResult {
  correlationId: string
  success:       boolean
  data?:         unknown
  error?:        { errorCode: string; description: string; httpStatusCode: number }
  metrics:       { durationMs: number; tokensUsed: number }
  timestamp:     string
}
```

### App.tsx — Swagger link + tenant label

```tsx
import { BASE } from './api'

// In the header JSX:
<header className="app-header">
  <span className="app-title">ToolEngine</span>
  <div className="app-header-right">
    <span className="header-tenant">tenant: onebcg-default-tenant</span>
    <a href={`${BASE}/swagger`} target="_blank" rel="noopener noreferrer"
       className="swagger-link">
      API Docs ↗
    </a>
  </div>
</header>
```

### App.css — ONE BCG brand tokens

```css
:root {
  --red:        #CC2222;
  --dark-grey:  #595959;
  --near-black: #222222;
  --yellow:     #FFD700;
}
/* Font: Arsenal — import from Google Fonts in index.html */
/* No bold anywhere in the UI */

.swagger-link {
  font-size: 12px;
  color: var(--dark-grey);
  text-decoration: none;
  border: 1px solid var(--dark-grey);
  border-radius: 3px;
  padding: 3px 10px;
  transition: color 0.12s, border-color 0.12s;
}
.swagger-link:hover { color: #fff; border-color: #fff; }
```

### Component reference pattern — NO metadata prefix

```tsx
// ToolList.tsx — correct
<span>{tool.name}</span>
<span>{tool.version}</span>

// ToolInvoker.tsx — correct
await invokeTool(tool.namespace, tool.name, tool.version, parsedInput, token)

// WRONG — never use tool.metadata.xxx (no such property)
<span>{tool.metadata.name}</span>
```

---

## Phase 4 completion checklist

**API Host:**
- [ ] `Jwt:Secret` validated ≥ 32 bytes at startup — API refuses to start with short key (E7)
- [ ] `EnsureDeletedAsync` + `EnsureCreatedAsync` in development startup
- [ ] `onebcg-default-tenant` seeded with `AllowNamespace("*")` in development
- [ ] `MigrateAsync()` in production (not EnsureCreated)
- [ ] `app.UseRateLimiter()` before `app.UseAuthentication()` in middleware pipeline (E3)
- [ ] `/dev/token` defaults to `"onebcg-default-tenant"` (NOT `"acme-corp"`) (M5)
- [ ] `GET /tools` returns flat `ToolSummaryResponse[]` — no nested metadata wrapper
- [ ] `202` responses include `Location` header and `Retry-After: 10` (E6)
- [ ] `Approval:BaseUrl` validated for HTTPS in non-development environments (E7)
- [ ] OTel tracing wired to `ActivitySource(ToolEngineTelemetry.ServiceName)` (G1)
- [ ] `TenantClaimsTransformer` adds `caller_type = Human` as default claim (H4)
- [ ] Swagger UI available at `/swagger` in development
- [ ] CORS enabled in development only

**CLI Host:**
- [ ] `CreateAsyncScope()` + `await using` in startup (NOT `CreateScope()` + `using`)
- [ ] `EnsureDeletedAsync` + `EnsureCreatedAsync` (NOT just `EnsureCreated`)
- [ ] `onebcg-default-tenant` seeded after schema creation
- [ ] `ConsoleApprovalGate` registered as `IHumanApprovalGate` (not `AsyncApprovalGate`)
- [ ] REPL supports: `list`, `invoke`, `ask`, `chat`, `chat end`, `exit`
- [ ] Default LLM provider in CLI appsettings is `"ollama"` (zero API cost for dev)

**Frontend:**
- [ ] `BASE` is `export const` (not just local const) — used by App.tsx for Swagger link
- [ ] `invokeTool` has `res.ok` guard — throws on 4xx/5xx (M7)
- [ ] `ToolDescriptor` is flat — NO `metadata` sub-object (M6)
- [ ] All component references use `tool.name`, `tool.namespace`, `tool.version` directly
- [ ] ONE BCG brand CSS variables defined: `--red #CC2222`, `--dark-grey #595959`, `--near-black #222222`, `--yellow #FFD700`
- [ ] Font: Arsenal — no bold used anywhere in the UI
- [ ] Swagger link in header uses `BASE` from `api.ts`

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
