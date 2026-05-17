using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using ToolEngine.Api.Auth;
using ToolEngine.Api.Endpoints;
using ToolEngine.Api.Middleware;
using ToolEngine.Application.Extensions;
using ToolEngine.Infrastructure.Extensions;
using ToolEngine.Tools.Executor.Extensions;
using ToolEngine.Tools.Registry.Extensions;
using ToolEngine.Tools.Samples.Extensions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // ── Authentication ───────────────────────────────────────────────────────
    var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
              ?? throw new InvalidOperationException("Jwt settings are missing.");

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

    // ── Infrastructure ───────────────────────────────────────────────────────
    var connStr = builder.Configuration.GetConnectionString("Default")
                  ?? "Data Source=toolengine-dev.db";

    builder.Services.AddToolInfrastructure(
        opt => opt.UseSqlite(connStr));

    // ── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── Swagger (dev only) ───────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // ── CORS (dev only) ──────────────────────────────────────────────────────
    builder.Services.AddCors(opt =>
        opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    var app = builder.Build();

    // ── Database init (dev only) ─────────────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider
                      .GetRequiredService<ToolEngine.Infrastructure.Persistence.AppDbContext>();
        db.Database.EnsureCreated();
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
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Endpoints ────────────────────────────────────────────────────────────
    app.MapToolEndpoints();
    app.MapApprovalEndpoints();
    app.MapInvocationEndpoints();
    app.MapHealthEndpoints();
    app.MapDevEndpoints();

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
