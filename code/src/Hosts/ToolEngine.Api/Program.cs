using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using ToolEngine.Api.Auth;
using ToolEngine.Api.Configuration;
using ToolEngine.Api.Endpoints;
using ToolEngine.Api.Middleware;
using ToolEngine.Application.Extensions;
using ToolEngine.Application.Telemetry;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Constants;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Infrastructure.Approval;
using ToolEngine.Infrastructure.Extensions;
using ToolEngine.Tools.Executor.Extensions;
using ToolEngine.Tools.Registry.Extensions;
using ToolEngine.Tools.Samples.Extensions;
using ToolEngine.Llm.Extensions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Structured logging with PII masking ──────────────────────────────────
    // Email addresses (approver emails from the approval pipeline) are masked so they
    // never appear in plaintext in log streams or third-party aggregators (GDPR Art. 5(1)(f)).
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        // Mask RFC-5321-like email addresses in structured log properties.
        // The regex is scoped to strings that have no whitespace, contain '@',
        // and have a dot after '@' — this avoids masking tool inputs that contain
        // '@' but are not email addresses (e.g. npm scopes, Slack @mentions).
        .Destructure.ByTransforming<string>(s =>
        {
            // Fast-path: skip strings with no '@'.
            var atIdx = s.IndexOf('@');
            if (atIdx < 0) return s;

            // Only mask if the string looks like a standalone email address:
            // no whitespace, at least one char before '@', a dot somewhere after '@'.
            if (s.Contains(' ') || s.Contains('\t') || s.Contains('\n')) return s;
            var afterAt = s.AsSpan(atIdx + 1);
            if (!afterAt.Contains('.')) return s;

            // Mask: keep first 2 chars of local-part, hide rest.
            var localPart = s[..atIdx];
            var prefix    = localPart.Length >= 2 ? localPart[..2] : localPart[..1];
            return $"{prefix}***@***.***";
        }));

    // ── Authentication ───────────────────────────────────────────────────────
    var jwt = builder.Configuration.GetSection(ConfigKeys.Jwt).Get<JwtSettings>()
              ?? throw new InvalidOperationException("Jwt settings are missing.");

    // Enforce a minimum key length of 32 bytes (256 bits) for HMAC-SHA256.
    // Shorter keys are vulnerable to brute-force offline attacks against captured JWTs.
    if (Encoding.UTF8.GetBytes(jwt.Secret).Length < 32)
        throw new InvalidOperationException(
            "Jwt:Secret must be at least 32 bytes (256 bits). " +
            "Generate a secure key: openssl rand -base64 32");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opt =>
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = jwt.Issuer,
                ValidAudience            = jwt.Audience,
                IssuerSigningKey         = new SymmetricSecurityKey(
                                               Encoding.UTF8.GetBytes(jwt.Secret))
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddSingleton(jwt);
    builder.Services.AddTransient<
        Microsoft.AspNetCore.Authentication.IClaimsTransformation,
        TenantClaimsTransformer>();

    // ── Tool engine ──────────────────────────────────────────────────────────
    builder.Services.AddToolRegistry();
    builder.Services.AddToolSamples();
    builder.Services.AddToolExecutor();
    builder.Services.AddToolApplication();
    builder.Services.AddToolLlm(builder.Configuration);

    // ── Infrastructure — modular DB + cache provider ─────────────────────────
    var connStr   = builder.Configuration.GetConnectionString(ConfigKeys.DefaultConnection)
                   ?? "Data Source=toolengine-dev.db";
    var dbOpts    = builder.Configuration.GetSection(ConfigKeys.Database).Get<DatabaseOptions>()
                   ?? new DatabaseOptions();
    var cacheOpts = builder.Configuration.GetSection(ConfigKeys.Cache).Get<CacheOptions>()
                   ?? new CacheOptions();

    // DB provider: "sqlite" (default/dev), "postgresql", "sqlserver"
    builder.Services.AddToolInfrastructure(opt =>
    {
        switch (dbOpts.Provider.ToLowerInvariant())
        {
            case ProviderNames.PostgreSql:
                opt.UseNpgsql(builder.Configuration.GetConnectionString(ConfigKeys.DefaultConnection)
                              ?? "Host=localhost;Database=toolengine");
                break;
            case ProviderNames.SqlServer:
                opt.UseSqlServer(builder.Configuration.GetConnectionString(ConfigKeys.DefaultConnection)
                                 ?? "Server=.;Database=ToolEngine;Trusted_Connection=True");
                break;
            default: // sqlite
                opt.UseSqlite(connStr);
                break;
        }
    });

    // Cache provider: "memory" (default/dev) or "redis" (production).
    // Redis is required when running more than one pod — memory cache is node-local.
    // Register Redis before AddToolInfrastructure so its fallback skips MemoryCacheProvider.
    if (cacheOpts.Provider.Equals(ProviderNames.Redis, StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddStackExchangeRedisCache(opt =>
            opt.Configuration =
                builder.Configuration.GetConnectionString(ConfigKeys.RedisConnection) ?? "localhost:6379");
        builder.Services.AddDistributedCacheProvider();
    }
    // Memory provider: AddToolInfrastructure registers it as a fallback if no ICacheProvider is yet registered.

    // ── Rate limiting ────────────────────────────────────────────────────────
    // Sliding-window rate limit on OTP verify: 10 attempts per IP per 10 minutes.
    // A per-entity failed-attempt counter on PendingApproval handles targeted attacks
    // against a specific approval token (OWASP MFA Cheat Sheet).
    builder.Services.AddRateLimiter(opt =>
    {
        opt.AddPolicy(RateLimitPolicies.OtpVerify, ctx =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    Window              = TimeSpan.FromMinutes(ServiceLimits.OtpRateLimitWindowMinutes),
                    SegmentsPerWindow   = 5,
                    PermitLimit         = ServiceLimits.OtpRateLimitPermitLimit,
                    QueueLimit          = 0
                }));

        opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        opt.OnRejected = async (ctx, token) =>
        {
            ctx.HttpContext.Response.Headers[HttpHeaderNames.RetryAfter] = "60";
            await ctx.HttpContext.Response.WriteAsJsonAsync(
                new { error = "TOO_MANY_REQUESTS",
                      description = "Too many OTP verification attempts. Try again in 10 minutes." },
                token);
        };
    });

    // ── OpenTelemetry — tracing and metrics ──────────────────────────────────
    // W3C traceparent/tracestate headers are propagated automatically by the
    // AspNetCore instrumentation. Configure "Otlp:Endpoint" to export to a collector
    // (e.g. http://otel-collector:4317).
    var otlpEndpoint = builder.Configuration[ConfigKeys.OtlpEndpoint];

    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(r => r
            .AddService(
                serviceName:    ToolEngineTelemetry.ServiceName,
                serviceVersion: ToolEngineTelemetry.ServiceVersion))
        .WithTracing(tracing =>
        {
            tracing
                .AddSource(ToolEngineTelemetry.ServiceName)   // custom tool execution spans
                .AddAspNetCoreInstrumentation()               // HTTP request spans
                .AddHttpClientInstrumentation()               // outbound HTTP spans (webhooks)
                .AddEntityFrameworkCoreInstrumentation();     // DB query spans

            if (!string.IsNullOrEmpty(otlpEndpoint))
                tracing.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddMeter(ToolEngineTelemetry.ServiceName)    // custom tool metrics
                .AddAspNetCoreInstrumentation();              // HTTP request metrics

            if (!string.IsNullOrEmpty(otlpEndpoint))
                metrics.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
        });

    // ── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── Swagger (dev only) ───────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // ── CORS (dev only) ──────────────────────────────────────────────────────
    builder.Services.AddCors(opt =>
        opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    var app = builder.Build();

    // ── Startup security validation ──────────────────────────────────────────
    // Fail fast on misconfiguration before accepting any traffic.
    // Magic links sent over HTTP expose the approval token in network traffic and logs.
    if (!app.Environment.IsDevelopment())
    {
        var approvalOpts = app.Services
            .GetRequiredService<IOptions<ApprovalOptions>>().Value;
        if (!approvalOpts.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Approval:BaseUrl must use HTTPS in non-development environments. " +
                "Magic links sent over HTTP are vulnerable to interception.");
    }

    // ── Database init ────────────────────────────────────────────────────────
    // Development: EnsureDeleted + EnsureCreated — always rebuilds the SQLite schema
    //   from the current model on startup. This prevents stale-schema errors
    //   whenever new entities are added. Dev data is intentionally ephemeral.
    //
    // Production: MigrateAsync — applies pending EF Core migrations. Create the
    //   initial migration before first production deploy:
    //     dotnet ef migrations add InitialCreate \
    //       --project src/Infrastructure/ToolEngine.Infrastructure \
    //       --startup-project src/Hosts/ToolEngine.Api
    {
        using var dbScope = app.Services.CreateScope();
        var db = dbScope.ServiceProvider
                        .GetRequiredService<ToolEngine.Infrastructure.Persistence.AppDbContext>();
        if (app.Environment.IsDevelopment())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // Seed a default dev tenant so the tool invocation pipeline can resolve
            // "onebcg-default-tenant" without requiring a separate setup step.
            // AllowNamespace("*") grants access to all registered namespaces.
            var clock = dbScope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            var devTenant = Tenant.Create("onebcg-default-tenant", "ONE BCG Default Tenant", "dev-seed", clock).Value;
            devTenant.AllowNamespace("*");
            db.Set<Tenant>().Add(devTenant);
            await db.SaveChangesAsync();
        }
        else
            await db.Database.MigrateAsync();
    }

    // ── Middleware pipeline ──────────────────────────────────────────────────
    app.UseMiddleware<GlobalExceptionMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors();
    }

    app.UseSerilogRequestLogging();
    app.UseRateLimiter();          // Must be before auth/routing so rate limits apply to all requests
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Endpoints ────────────────────────────────────────────────────────────
    app.MapToolEndpoints();
    app.MapApprovalEndpoints();
    app.MapInvocationEndpoints();
    app.MapHealthEndpoints();
    app.MapDevEndpoints();
    app.MapAgentEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ToolEngine.Api terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
