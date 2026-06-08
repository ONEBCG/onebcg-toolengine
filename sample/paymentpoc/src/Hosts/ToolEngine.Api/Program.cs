using System.Text;
using Amazon;
using Microsoft.Extensions.Options;
using ToolEngine.Infrastructure.Database;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ToolEngine.Api.Services;
using ToolEngine.Application.Extensions;
using ToolEngine.Infrastructure.Extensions;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Infrastructure;
using ToolEngine.Payment.Api.Extensions;
using ToolEngine.Tools.Executor.Extensions;
using ToolEngine.Tools.Registry.Extensions;
using RegisterPaymentModule = ToolEngine.Payment.Api.Extensions.ServiceCollectionExtensions;

// ── AWS Secrets Manager — inject production secrets before builder ─────────────
// Runs only when ASPNETCORE_ENVIRONMENT=Production (i.e. on Lambda).
// Double-underscore maps to IConfiguration hierarchy:
//   Database__ConnectionString → "Database:ConnectionString" config key
//   Jwt__SecretKey             → "Jwt:SecretKey" config key
// ANTHROPIC_API_KEY is read directly by ClaudeProvider via GetEnvironmentVariable.
if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
{
    // AWS_DEFAULT_REGION is set automatically by Lambda at runtime.
    // Fallback to ap-south-1 matches our deployment region.
    var awsRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "ap-south-1";
    var smClient  = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(awsRegion));

    static async Task<string> FetchSecret(AmazonSecretsManagerClient client, string name)
    {
        var resp = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = name });
        return resp.SecretString;
    }

    Environment.SetEnvironmentVariable("Database__ConnectionString",
        await FetchSecret(smClient, "toolengine/db-connection"));

    Environment.SetEnvironmentVariable("Jwt__SecretKey",
        await FetchSecret(smClient, "toolengine/jwt-secret"));

    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY",
        await FetchSecret(smClient, "toolengine/anthropic-api-key"));

    Environment.SetEnvironmentVariable("GOOGLE_API_KEY",
        await FetchSecret(smClient, "toolengine/google-api-key"));
}

var builder = WebApplication.CreateBuilder(args);
var config  = builder.Configuration;

// ── Lambda hosting — replaces Kestrel when running on AWS Lambda ──────────────
// Transparent to all other code — the ASP.NET Core pipeline is identical.
// SSE streaming (/api/v1/chat/stream) is enabled by setting the Lambda Function URL
// invoke mode to RESPONSE_STREAM in aws-lambda-tools-defaults.json — no code flag needed.
// When AWS_LAMBDA_FUNCTION_NAME is absent the app starts normally with Kestrel.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
    builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// ── Validate JWT secret length at startup (≥ 32 bytes — Phase E) ─────────────
var jwtSecret = config["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey is required.");

if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException(
        "Jwt:SecretKey must be at least 32 bytes (256 bits). " +
        "Current value is too short — update appsettings.");

// ── Services ──────────────────────────────────────────────────────────────────

// Infrastructure (EF Core — SQL Server, UnitOfWork, Cache, Vault, Clock)
builder.Services.AddToolInfrastructure(config);

// Tool engine kernel
builder.Services.AddToolRegistry();
builder.Services.AddToolExecutor();

// Application layer: MediatR + 7 pipeline behaviors in mandatory order
builder.Services.AddApplicationLayer(
    typeof(ToolEngine.Application.Commands.ExecuteToolCommand).Assembly);

// HTTP client factory (required by ApiToolBase implementations)
builder.Services.AddHttpClient();

// Chat service — Claude API agentic tool loop
builder.Services.AddScoped<ChatService>();

// MVC Controllers — register UndefinedJsonElementConverter here too.
// ConfigureHttpJsonOptions only covers Minimal API responses; MVC ObjectResult
// uses its own JsonSerializerOptions configured via AddControllers().AddJsonOptions().
builder.Services.AddControllers()
    .AddApplicationPart(typeof(ToolEngine.Payment.Api.Controllers.PaymentsController).Assembly)
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new UndefinedJsonElementConverter()));

// Scenario Orchestration Layer — IScenarioRegistry, IToolPlanOrchestrator, ScenarioRunner
builder.Services.AddScenarioOrchestration();

// Payment module — tools, scenarios, MediatR handlers, EF configs, seeder
builder.Services.AddPaymentModule();

// ── JWT Authentication ────────────────────────────────────────────────────────
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = config["Jwt:Issuer"],
            ValidAudience            = config["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),
            // H5: pin accepted algorithm — prevents algorithm-confusion (e.g. alg=none, RS256 key-confusion)
            ValidAlgorithms          = new[] { SecurityAlgorithms.HmacSha256 },
        };
    });

builder.Services.AddAuthorization();

// ── CORS — allow the standalone UI project ────────────────────────────────────
// UI runs on :5001, API on :5000. AllowedOrigins is configurable via appsettings.
var allowedOrigins = (config["Cors:AllowedOrigins"] ?? "http://localhost:5001")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(opts =>
    opts.AddPolicy("UiPolicy", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()));

// ── Rate Limiting (IP-based, Phase E) ────────────────────────────────────────
builder.Services.AddRateLimiter(opts =>
{
    opts.AddFixedWindowLimiter("fixed", o =>
    {
        o.Window             = TimeSpan.Parse(config["RateLimit:Window"] ?? "00:01:00");
        o.PermitLimit        = int.Parse(config["RateLimit:PermitLimit"] ?? "60");
        o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        o.QueueLimit         = int.Parse(config["RateLimit:QueueLimit"] ?? "0");
    });

    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── JSON serialisation — handle default(JsonElement) gracefully ───────────────
// ToolResponse<JsonElement>.Data is a struct. Fail() and Suspend() never set it,
// so it stays default (ValueKind=Undefined). The built-in JsonElementConverter
// throws InvalidOperationException on Undefined values — write null instead.
builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.Converters.Add(new UndefinedJsonElementConverter()));

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "ToolEngine Payment POC API",
        Version     = "v1",
        Description = "ONE BCG ToolEngine v2026 — B2B Payment Processing Pipeline (7-Stage)",
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In          = ParameterLocation.Header,
        Description = "JWT Bearer token — format: 'Bearer {token}'",
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Migrate DB + Seed (dev & staging) ────────────────────────────────────────
// Resolve DB options outside the scope so we can access them after scope disposal.
var _dbOpts = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

// Resolve the SQLite file path once — used for self-heal delete below.
static string? ResolveSqliteFile(string connectionString) =>
    connectionString.Split(';')
        .Select(p => p.Trim())
        .FirstOrDefault(p => p.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        ?.Substring("Data Source=".Length)
        .Trim();

bool _migrationOk = false;
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        // PostgreSQL: if payment tables were created with SQLite type hints (TEXT for
        // uuid/timestamp, INTEGER for boolean), drop and recreate them so MigrateAsync
        // can rebuild with the correct PostgreSQL types. Detected via PayeeRecords.Id
        // being 'text' instead of 'uuid'. Safe no-op once tables are correctly typed.
        if (_dbOpts.Provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name  = 'PayeeRecords'
                          AND column_name = 'Id'
                          AND data_type   = 'text'
                    ) THEN
                        DROP TABLE IF EXISTS ""PaymentAuditLogs""   CASCADE;
                        DROP TABLE IF EXISTS ""KycScreeningRecords"" CASCADE;
                        DROP TABLE IF EXISTS ""PaymentInstructions"" CASCADE;
                        DROP TABLE IF EXISTS ""WhtRateEntries""      CASCADE;
                        DROP TABLE IF EXISTS ""PpmContracts""        CASCADE;
                        DROP TABLE IF EXISTS ""PayeeRecords""        CASCADE;
                        DELETE FROM ""__EFMigrationsHistory""
                        WHERE ""MigrationId"" IN (
                            '20260608000000_AddPaymentTables',
                            '20260608000001_FixPostgresColumnTypes'
                        );
                    END IF;
                END $$;
            ");
        }

        await db.Database.MigrateAsync();
        var seeders = scope.ServiceProvider.GetServices<IModuleSeeder>();
        foreach (var seeder in seeders)
            await seeder.SeedAsync(db, logger);
        logger.LogInformation("Database migration and seeding completed.");
        _migrationOk = true;
    }
    catch (Exception ex) when (
        // SQLite: stale DB whose schema predates the migrations history table
        // (created via EnsureCreated, or __EFMigrationsHistory was wiped).
        // Signal outer block to delete the file — cannot delete here because the
        // scope still holds EF's internal connection pool open.
        _dbOpts.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning(
            "SQLite migration conflict — table already exists. " +
            "Scope will be disposed to release file locks, then DB will be reset.");
        // Do NOT rethrow — let the scope dispose cleanly, then handle below.
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration/seeding failed. Check connection string.");
        throw;
    }
} // <-- scope disposed here: EF DbContext + internal SQLite connections released

// Self-heal: scope is gone, connection pool flushed — safe to delete the file now.
if (!_migrationOk &&
    _dbOpts.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var file   = ResolveSqliteFile(_dbOpts.ConnectionString);

    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
    {
        // SQLite may still pool connections globally — clear all pools before delete.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(file);
        logger.LogWarning("Deleted stale SQLite file: {File}. Recreating from migrations.", file);
    }

    // Fresh scope — DB file is gone, MigrateAsync will create it from scratch.
    using var retryScope  = app.Services.CreateScope();
    var retryDb     = retryScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var retryLogger = retryScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await retryDb.Database.MigrateAsync();
        var seeders = retryScope.ServiceProvider.GetServices<IModuleSeeder>();
        foreach (var seeder in seeders)
            await seeder.SeedAsync(retryDb, retryLogger);
        retryLogger.LogInformation("Database migration and seeding completed after reset.");
    }
    catch (Exception retryEx)
    {
        retryLogger.LogError(retryEx, "Database migration/seeding failed after SQLite reset.");
        throw;
    }
}

// ── Register payment module (tool descriptors + scenario definitions) ─────────
// Must run after DI container is built (uses CreateScope internally)
var scenarioRegistry = app.Services.GetRequiredService<IScenarioRegistry>();
await RegisterPaymentModule.RegisterPaymentModuleAsync(app.Services, scenarioRegistry);

// ── Middleware pipeline ───────────────────────────────────────────────────────

// Global exception handler — always returns JSON so the UI can parse errors.
// Must be registered first (outermost middleware) to catch all downstream exceptions.
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    ctx.Response.ContentType = "application/json";
    ctx.Response.StatusCode  = 500;
    await ctx.Response.WriteAsJsonAsync(new
    {
        error   = ex?.GetType().FullName,
        message = ex?.Message,
        inner   = ex?.InnerException?.Message,
        // Stack trace visible in Development only
        stack   = app.Environment.IsDevelopment() ? ex?.StackTrace : null,
    });
}));

// Swagger available in all environments for POC — API Gateway routes /swagger → this Lambda
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ToolEngine Payment POC v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("UiPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoint registration ─────────────────────────────────────────────────────
app.MapControllers();

// API root → redirect to Swagger
app.MapGet("/api", () => Results.Redirect("/swagger")).AllowAnonymous();

// Health check (no auth required)
app.MapGet("/health", () => Results.Ok(new
{
    status  = "healthy",
    version = "v2026-poc",
    utcNow  = DateTimeOffset.UtcNow,
}))
.WithTags("Health")
.AllowAnonymous();

app.Run();

/// <summary>
/// Writes null for <see cref="JsonValueKind.Undefined"/> elements instead of throwing.
/// ToolResponse.Data is a JsonElement struct; Fail() and Suspend() leave it as default,
/// which is Undefined — not serialisable by the built-in converter.
/// </summary>
sealed class UndefinedJsonElementConverter : System.Text.Json.Serialization.JsonConverter<System.Text.Json.JsonElement>
{
    public override System.Text.Json.JsonElement Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
        => System.Text.Json.JsonDocument.ParseValue(ref reader).RootElement;

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        System.Text.Json.JsonElement value,
        System.Text.Json.JsonSerializerOptions options)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            writer.WriteNullValue();
        else
            value.WriteTo(writer);
    }
}

