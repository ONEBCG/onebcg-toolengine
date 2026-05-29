using System.Text;
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

var builder = WebApplication.CreateBuilder(args);
var config  = builder.Configuration;

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
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.MigrateAsync();

        var seeders = scope.ServiceProvider.GetServices<IModuleSeeder>();
        foreach (var seeder in seeders)
            await seeder.SeedAsync(db, logger);

        logger.LogInformation("Database migration and seeding completed.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration/seeding failed. Check connection string.");
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ToolEngine Payment POC v1");
        c.RoutePrefix = "swagger";  // Swagger UI at /swagger; root serves demo UI
    });
}

app.UseCors("UiPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoint registration ─────────────────────────────────────────────────────
app.MapControllers();

// Redirect root to Swagger UI — API no longer serves the demo UI
app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous();

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

