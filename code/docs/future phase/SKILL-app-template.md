---
name: toolengine-app-template
description: >
  Core scaffold template for any new ONE BCG microservice or application built
  on the ToolEngine platform. Produces a production-ready service skeleton in
  under 5 minutes: layered clean architecture with CQRS/MediatR, DI wiring,
  JWT + OAuth 2.1 auth, health checks (live/ready/startup), OpenAPI with Swagger
  UI, Serilog structured logging, OpenTelemetry, EF Core with multi-provider
  support, Docker multi-stage build, and GitHub Actions CI/CD pipeline.
  Use this template as the mandatory starting point for all new ONE BCG services.
classification: Confidential - Internal Use Only
---

# Application Template — ONE BCG Development Platform

## Purpose

Every new ONE BCG service starts from this template. It encodes the conventions
and patterns established across all ToolEngine SKILL files. Starting from this
template guarantees: correct layer separation, security baseline, observability,
CI/CD integration, and SonarCloud coverage gate — on day 1.

**Time to first green build: < 5 minutes.**

---

## 1. Repository Scaffold

### CLI commands — run from the solution root

```bash
# Replace 'Payments' with your service name throughout
SERVICE=Payments
SLUG=payments

# Solution
dotnet new sln -n "OneBCG.${SERVICE}"

# Projects
dotnet new classlib -n "OneBCG.${SERVICE}.Core.Abstractions" \
  -o "src/Core/OneBCG.${SERVICE}.Core.Abstractions" --framework net8.0
dotnet new classlib -n "OneBCG.${SERVICE}.Core.Domain" \
  -o "src/Core/OneBCG.${SERVICE}.Core.Domain" --framework net8.0
dotnet new classlib -n "OneBCG.${SERVICE}.Application" \
  -o "src/Application/OneBCG.${SERVICE}.Application" --framework net8.0
dotnet new classlib -n "OneBCG.${SERVICE}.Infrastructure" \
  -o "src/Infrastructure/OneBCG.${SERVICE}.Infrastructure" --framework net8.0
dotnet new webapi   -n "OneBCG.${SERVICE}.Api" \
  -o "src/Hosts/OneBCG.${SERVICE}.Api" --framework net8.0 --no-openapi

# Test projects
dotnet new xunit -n "OneBCG.${SERVICE}.Application.Tests" \
  -o "tests/OneBCG.${SERVICE}.Application.Tests" --framework net8.0
dotnet new xunit -n "OneBCG.${SERVICE}.Api.Tests" \
  -o "tests/OneBCG.${SERVICE}.Api.Tests" --framework net8.0

# Add all to solution
for proj in \
  src/Core/OneBCG.${SERVICE}.Core.Abstractions \
  src/Core/OneBCG.${SERVICE}.Core.Domain \
  src/Application/OneBCG.${SERVICE}.Application \
  src/Infrastructure/OneBCG.${SERVICE}.Infrastructure \
  src/Hosts/OneBCG.${SERVICE}.Api \
  tests/OneBCG.${SERVICE}.Application.Tests \
  tests/OneBCG.${SERVICE}.Api.Tests; do
  dotnet sln add $proj
done

# Project references (dependency flow: only inward)
dotnet add src/Core/OneBCG.${SERVICE}.Core.Domain reference \
  src/Core/OneBCG.${SERVICE}.Core.Abstractions
dotnet add src/Application/OneBCG.${SERVICE}.Application reference \
  src/Core/OneBCG.${SERVICE}.Core.Domain
dotnet add src/Infrastructure/OneBCG.${SERVICE}.Infrastructure reference \
  src/Application/OneBCG.${SERVICE}.Application
dotnet add src/Hosts/OneBCG.${SERVICE}.Api reference \
  src/Infrastructure/OneBCG.${SERVICE}.Infrastructure

# Test references
dotnet add tests/OneBCG.${SERVICE}.Application.Tests reference \
  src/Application/OneBCG.${SERVICE}.Application
dotnet add tests/OneBCG.${SERVICE}.Api.Tests reference \
  src/Hosts/OneBCG.${SERVICE}.Api

# Remove template boilerplate
rm src/Hosts/OneBCG.${SERVICE}.Api/WeatherForecast.cs 2>/dev/null || true
rm src/Hosts/OneBCG.${SERVICE}.Api/Controllers/WeatherForecastController.cs 2>/dev/null || true
```

---

## 2. Project Files

### All projects — shared `Directory.Build.props` (repository root)

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <AnalysisMode>Recommended</AnalysisMode>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <Optimize>true</Optimize>
    <DebugType>pdbonly</DebugType>
  </PropertyGroup>
</Project>
```

### `Core.Abstractions.csproj` — zero NuGet dependencies

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <!-- No PackageReference entries — BCL only -->
</Project>
```

### `Core.Domain.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Core\OneBCG.${SERVICE}.Core.Abstractions\..." />
    <PackageReference Include="System.Text.Json" Version="8.*" />
  </ItemGroup>
</Project>
```

### `Application.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Core\OneBCG.${SERVICE}.Core.Domain\..." />
    <PackageReference Include="MediatR"                         Version="12.*" />
    <PackageReference Include="FluentValidation"               Version="11.*" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.*" />
  </ItemGroup>
</Project>
```

### `Infrastructure.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Application\OneBCG.${SERVICE}.Application\..." />
    <PackageReference Include="Microsoft.EntityFrameworkCore"          Version="8.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite"   Version="8.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.*" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL"   Version="8.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools"     Version="8.*" PrivateAssets="all" />
    <PackageReference Include="StackExchange.Redis"                     Version="2.*" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience"   Version="8.*" />
  </ItemGroup>
</Project>
```

### `Api.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <UserSecretsId>onebcg-${SLUG}-dev</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Infrastructure\OneBCG.${SERVICE}.Infrastructure\..." />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer"    Version="8.*" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="8.*" />
    <PackageReference Include="Serilog.AspNetCore"                               Version="8.*" />
    <PackageReference Include="Serilog.Sinks.Console"                            Version="5.*" />
    <PackageReference Include="Serilog.Sinks.OpenTelemetry"                      Version="3.*" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting"                 Version="1.*" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore"         Version="1.*" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http"               Version="1.*" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol"     Version="1.*" />
    <PackageReference Include="Swashbuckle.AspNetCore"                           Version="6.*" />
    <PackageReference Include="Scalar.AspNetCore"                                Version="1.*" />
  </ItemGroup>
</Project>
```

---

## 3. Core Abstractions

### Minimum required interfaces — `src/Core/Core.Abstractions/`

```
Common/
  IDateTimeProvider.cs
  ICurrentUser.cs
Persistence/
  IRepository.cs
  IReadRepository.cs
  IUnitOfWork.cs
Cache/
  ICacheProvider.cs
Secrets/
  ISecretVault.cs
```

These are identical to ToolEngine's. Copy from the ToolEngine SDK NuGet
package (`OneBCG.ToolEngine.Sdk`) or replicate inline. Never modify
the contracts — consistency across services is mandatory.

---

## 4. `Program.cs` Template

```csharp
using Serilog;
using OneBCG.Payments.Api.Extensions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Logging ────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "Payments")
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {Message:lj}{NewLine}{Exception}"));

    // ── Configuration ──────────────────────────────────────────────────────
    builder.Configuration
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables()
        .AddUserSecrets<Program>(optional: true);

    // ── Core Services ──────────────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddApplicationServices();          // MediatR + Validators
    builder.Services.AddInfrastructureServices(         // EF Core + Cache + Repos
        builder.Configuration);

    // ── Authentication & Authorization ─────────────────────────────────────
    builder.Services.AddAuthServices(builder.Configuration);

    // ── OpenAPI ────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new() { Title = "ONE BCG Payments API", Version = "v1" });
        opts.AddSecurityDefinition("Bearer", new()
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        opts.AddSecurityRequirement(new()
        {
            [new() { Reference = new() { Id = "Bearer", Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme } }]
                = Array.Empty<string>()
        });
    });

    // ── Observability ──────────────────────────────────────────────────────
    builder.Services.AddObservability(builder.Configuration);

    // ── Rate Limiting ──────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(opts =>
    {
        opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // Add sliding window policies here
    });

    // ── Health Checks ──────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database")
        .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost")
        .AddCheck("self", () => HealthCheckResult.Healthy());

    var app = builder.Build();

    // ── Middleware pipeline ─────────────────────────────────────────────────
    app.UseSerilogRequestLogging(opts =>
    {
        opts.EnrichDiagnosticContext = (ctx, httpCtx) =>
        {
            ctx.Set("TenantId", httpCtx.User.FindFirst("tenant_id")?.Value ?? "unknown");
            ctx.Set("UserId",   httpCtx.User.FindFirst("sub")?.Value ?? "anonymous");
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(opts => opts.SwaggerEndpoint("/swagger/v1/swagger.json", "Payments v1"));
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // ── Health endpoints ────────────────────────────────────────────────────
    app.MapHealthChecks("/healthz/live", new()
        { Predicate = check => check.Name == "self" });
    app.MapHealthChecks("/healthz/ready", new()
        { Predicate = _ => true });
    app.MapHealthChecks("/healthz/startup", new()
        { Predicate = check => check.Tags.Contains("startup") });

    // ── API Endpoints ───────────────────────────────────────────────────────
    app.MapPaymentsEndpoints();    // defined in Endpoints/

    // ── Database migration ──────────────────────────────────────────────────
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (app.Environment.IsDevelopment())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.MigrateAsync();
        }
    }

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
```

---

## 5. Service Registration Extensions

### `ApplicationServiceExtensions.cs` — `Application/Extensions/`

```csharp
public static IServiceCollection AddApplicationServices(
    this IServiceCollection services)
{
    services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    });

    services.AddValidatorsFromAssembly(
        typeof(ApplicationAssemblyMarker).Assembly,
        includeInternalTypes: true);

    return services;
}
```

### `InfrastructureServiceExtensions.cs` — `Infrastructure/Extensions/`

```csharp
public static IServiceCollection AddInfrastructureServices(
    this IServiceCollection services,
    IConfiguration config)
{
    var dbProvider  = config["Database:Provider"] ?? "sqlite";
    var connStr     = config.GetConnectionString("Default") ?? "Data Source=payments-dev.db";
    var cacheProvider = config["Cache:Provider"] ?? "memory";

    services.AddDbContext<AppDbContext>(opts =>
    {
        switch (dbProvider.ToLowerInvariant())
        {
            case "postgresql": opts.UseNpgsql(connStr); break;
            case "sqlserver":  opts.UseSqlServer(connStr); break;
            default:           opts.UseSqlite(connStr); break;
        }

        if (config.GetValue<bool>("Database:EnableSensitiveDataLogging"))
            opts.EnableSensitiveDataLogging();
    });

    if (cacheProvider == "redis")
    {
        services.AddStackExchangeRedisCache(opts =>
            opts.Configuration = config.GetConnectionString("Redis"));
        services.AddSingleton<ICacheProvider, DistributedCacheProvider>();
    }
    else
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheProvider, MemoryCacheProvider>();
    }

    services.AddScoped(typeof(IRepository<,>), typeof(Repository<,>));
    services.AddScoped(typeof(IReadRepository<,>), typeof(ReadRepository<,>));
    services.AddScoped<IUnitOfWork, UnitOfWork>();
    services.AddScoped<ISecretVault, EnvironmentSecretVault>();
    services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
    services.AddTransient<IDateTimeProvider, UtcDateTimeProvider>();

    return services;
}
```

### `ObservabilityExtensions.cs`

```csharp
public static IServiceCollection AddObservability(
    this IServiceCollection services,
    IConfiguration config)
{
    var otlpEndpoint = config["Otlp:Endpoint"];
    var serviceName  = config["Service:Name"] ?? "onebcg-service";

    services.AddOpenTelemetry()
        .ConfigureResource(r => r
            .AddService(serviceName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = config["ASPNETCORE_ENVIRONMENT"] ?? "production"
            }))
        .WithTracing(t =>
        {
            t.AddSource(serviceName)
             .AddAspNetCoreInstrumentation()
             .AddHttpClientInstrumentation()
             .AddEntityFrameworkCoreInstrumentation();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        })
        .WithMetrics(m =>
        {
            m.AddMeter(serviceName)
             .AddAspNetCoreInstrumentation()
             .AddHttpClientInstrumentation();

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });

    return services;
}
```

---

## 6. `appsettings.json` Template

```json
{
  "Service": { "Name": "onebcg-payments" },

  "Serilog": {
    "MinimumLevel": {
      "Default":    "Information",
      "Override":   { "Microsoft": "Warning", "System": "Warning" }
    }
  },

  "Jwt": {
    "Algorithm":        "RS256",
    "PublicKeyPath":    "/run/secrets/jwt-public.pem",
    "Issuer":           "https://auth.onebcg.com",
    "Audience":         "onebcg-payments",
    "ExpiryMinutes":    60,
    "ClockSkewSeconds": 30
  },

  "Database":  { "Provider": "sqlite" },
  "Cache":     { "Provider": "memory" },
  "Otlp":      { "Endpoint": "" },

  "AllowedHosts": "*"
}
```

### `appsettings.Development.json`

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Debug" }
  },

  "Jwt": {
    "Algorithm":    "HS256",
    "Secret":       "dev-secret-min-32-bytes-change-in-prod-!!",
    "Issuer":       "http://localhost:7001",
    "Audience":     "onebcg-payments-dev"
  },

  "ConnectionStrings": {
    "Default": "Data Source=payments-dev.db"
  },

  "Database":  { "Provider": "sqlite", "EnableSensitiveDataLogging": true },
  "Cache":     { "Provider": "memory" }
}
```

---

## 7. Docker Multi-Stage Build

### `Dockerfile`

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Restore dependencies separately for layer cache efficiency
COPY ["src/Core/",          "src/Core/"]
COPY ["src/Application/",   "src/Application/"]
COPY ["src/Infrastructure/","src/Infrastructure/"]
COPY ["src/Hosts/",         "src/Hosts/"]
COPY ["OneBCG.Payments.sln", "."]

RUN dotnet restore "OneBCG.Payments.sln"

# Build and publish
RUN dotnet publish "src/Hosts/OneBCG.Payments.Api/OneBCG.Payments.Api.csproj" \
    -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Stage 2: Runtime — minimal attack surface
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Non-root user (required by ONE BCG security policy)
RUN addgroup --system --gid 1000 appgroup && \
    adduser --system --uid 1000 --gid 1000 appuser

WORKDIR /app
COPY --from=build --chown=appuser:appgroup /app/publish .

# Read-only filesystem — writable mounts must be explicit
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:8080/healthz/live || exit 1

ENTRYPOINT ["dotnet", "OneBCG.Payments.Api.dll"]
```

### `docker-compose.yml` (development)

```yaml
version: '3.9'

services:
  api:
    build: .
    ports: ["7001:8080"]
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - Database__Provider=postgresql
      - Database__ConnectionString=Host=postgres;Database=payments;Username=dev;Password=dev
      - Cache__Provider=redis
      - ConnectionStrings__Redis=redis:6379
    depends_on:
      postgres: { condition: service_healthy }
      redis:    { condition: service_started }

  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB:       payments
      POSTGRES_USER:     dev
      POSTGRES_PASSWORD: dev
    volumes: [postgres-data:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD", "pg_isready", "-U", "dev"]
      interval: 5s
      timeout:  3s
      retries:  10

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

volumes:
  postgres-data:
```

---

## 8. GitHub Actions CI/CD Pipeline

### `.github/workflows/ci.yml`

```yaml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    name: Build, Test, Sonar

    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }

      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: ${{ runner.os }}-nuget-

      - name: Install tools
        run: |
          dotnet tool install --global dotnet-sonarscanner
          dotnet tool install --global dotnet-coverage
          dotnet tool install --global reportgenerator

      - name: Restore
        run: dotnet restore

      - name: Begin Sonar
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: |
          dotnet sonarscanner begin \
            /k:"onebcg_payments" \
            /o:"onebcg" \
            /d:sonar.token="${SONAR_TOKEN}" \
            /d:sonar.host.url="https://sonarcloud.io" \
            /d:sonar.cs.opencover.reportsPaths="**/coverage/coverage.opencover.xml" \
            /d:sonar.cs.vstest.reportsPaths="**/TestResults/*.trx"

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test with Coverage
        run: |
          dotnet-coverage collect \
            'dotnet test --configuration Release \
              --no-build \
              --logger trx \
              --results-directory TestResults' \
            -f xml -o coverage/coverage.opencover.xml

      - name: End Sonar
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: dotnet sonarscanner end /d:sonar.token="${SONAR_TOKEN}"

      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: |
            TestResults/
            coverage/

  docker:
    runs-on: ubuntu-latest
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    name: Docker Build & Push

    steps:
      - uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context:    .
          push:       true
          tags:       ghcr.io/onebcg/payments-api:${{ github.sha }}
          cache-from: type=gha
          cache-to:   type=gha,mode=max
```

---

## 9. New Service Checklist

### Repository setup
- [ ] Solution and project scaffold created via CLI commands in §1
- [ ] `Directory.Build.props` at repository root with `TreatWarningsAsErrors=true`
- [ ] `.gitignore` includes: `bin/`, `obj/`, `*.user`, `.env`, `*.db`, `secrets/`
- [ ] `sonar-project.properties` created (see SKILL-sonar-quality-gate.md)
- [ ] `SONAR_TOKEN` added to GitHub secrets

### Architecture
- [ ] Dependency flow: Api → Infrastructure → Application → Domain → Abstractions
- [ ] No project reference from Domain/Abstractions to Infrastructure
- [ ] No `new` on infrastructure types inside Application handlers
- [ ] `Result<T>` used for all business failures; exceptions for infrastructure failures only

### Security baseline
- [ ] JWT validation configured (RS256 in production, HS256 in dev only)
- [ ] Health endpoints (`/healthz/live`, `/healthz/ready`) return 200 without auth
- [ ] All other endpoints require `[Authorize]`
- [ ] No hardcoded secrets in `appsettings.json`
- [ ] `ISecretVault` wired for production secret access

### Observability baseline
- [ ] Serilog configured with `UseSerilogRequestLogging`
- [ ] OpenTelemetry with `ActivitySource` and `Meter` for the service
- [ ] OTel OTLP exporter configured (empty endpoint = disabled in dev)
- [ ] `TenantId` and `UserId` enriched on Serilog request log

### CI/CD
- [ ] `ci.yml` workflow with build, test, Sonar, Docker steps
- [ ] Sonar quality gate must pass before PR merge (GitHub branch protection)
- [ ] Docker image pushed to GHCR on `main` branch merge
- [ ] `Dockerfile` uses non-root user (`appuser:1000`)
- [ ] Multi-stage build: SDK for build, aspnet for runtime

### Quality gates
- [ ] `Directory.Build.props` has `TreatWarningsAsErrors=true`
- [ ] Coverage ≥ 80% (enforced by Sonar gate)
- [ ] Zero new bugs, zero new vulnerabilities (Sonar gate)
- [ ] Mutation score ≥ 70% (run Stryker locally before merge)

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
