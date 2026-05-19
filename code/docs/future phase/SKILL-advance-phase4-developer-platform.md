---
name: toolengine-advance-phase4-developer-platform
description: >
  Transforms ToolEngine v2026 from an internal service into a developer platform
  that external ONE BCG teams build upon. Covers: NuGet Tool SDK packaging for
  external tool authoring, TypeScript and Python client SDK generation via NSwag
  and Kiota, semantic tool versioning with breaking change detection, tool
  marketplace catalog UI, canary deployment for tool version rollout, AsyncAPI
  spec generation for event-driven surface documentation, and CLI scaffolding
  for rapid new tool project setup.
classification: Confidential - Internal Use Only
---

# Advancement Phase 4 — Developer Platform & Extensibility

## Prerequisites

Phase A1 (Security & Resilience) complete. Phases A2 and A3 are recommended
but not required. NuGet feed (Azure Artifacts or GitHub Packages) available
for SDK publishing.

---

## Overview

| Item | Description | Goal |
|------|-------------|------|
| A4.1 | NuGet Tool SDK | External tool authoring without monorepo |
| A4.2 | TypeScript + Python SDK generation | Eliminate hand-rolled API clients |
| A4.3 | Semantic versioning + breaking change detection | Contract safety for live tools |
| A4.4 | Tool marketplace catalog | Searchable tool discovery UI |
| A4.5 | Canary deployment for tool versions | Risk-free tool updates in production |
| A4.6 | AsyncAPI spec generation | Document event-driven surface |
| A4.7 | CLI scaffolding | 2-minute new tool project setup |

---

## A4.1 — NuGet Tool SDK (`OneBCG.ToolEngine.Sdk`)

### Why

External teams currently fork the monorepo to build tools. This causes version
drift, merge conflicts, and inconsistent patterns. Packaging the core abstractions
as a versioned NuGet SDK enables any .NET project to build ToolEngine-compatible
tools without touching the core repo.

### SDK project structure — `src/Sdk/OneBCG.ToolEngine.Sdk/`

```
OneBCG.ToolEngine.Sdk/
  Abstractions/
    ITool.cs                  — copied from Core.Abstractions
    IToolHandler.cs
    IToolRegistry.cs
  Base/
    LogicToolBase.cs          — pure computation base
    ApiToolBase.cs            — HTTP + ISecretVault base
    DatabaseToolBase.cs       — EF Core base
    CompositeToolBase.cs      — DAG orchestration base
  Domain/
    Result.cs                 — Result<T> railway pattern
    ToolRequest.cs
    ToolResponse.cs
    ToolError.cs
  Attributes/
    ToolAttribute.cs          — [Tool("namespace", "name", "1.0.0")]
    ApprovalRequiredAttribute.cs
  Validation/
    ToolInputValidator.cs     — FluentValidation base for tool inputs
  README.md
```

### SDK `.csproj` — `OneBCG.ToolEngine.Sdk.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <!-- NuGet package metadata -->
    <PackageId>OneBCG.ToolEngine.Sdk</PackageId>
    <Version>$(VersionPrefix)</Version>
    <Authors>ONE BCG</Authors>
    <Description>SDK for building ToolEngine-compatible tools for the ONE BCG platform.</Description>
    <PackageTags>toolengine;ai;tools;onebcg</PackageTags>
    <PackageLicenseExpression>Proprietary</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/onebcg/toolengine</RepositoryUrl>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>

  <!-- Minimal dependencies: only what external tool authors need -->
  <ItemGroup>
    <PackageReference Include="FluentValidation" Version="11.*" />
    <PackageReference Include="System.Text.Json" Version="8.*" />
  </ItemGroup>
</Project>
```

### `ToolAttribute.cs` — declarative tool registration

```csharp
namespace OneBCG.ToolEngine.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ToolAttribute : Attribute
{
    public string Namespace   { get; }
    public string Name        { get; }
    public string Version     { get; }
    public string Description { get; set; } = string.Empty;
    public ApprovalRisk Risk  { get; set; } = ApprovalRisk.Low;

    public ToolAttribute(string @namespace, string name, string version)
    {
        Namespace = @namespace;
        Name      = name;
        Version   = version;
    }
}
```

### External tool usage example

```csharp
// An external team's project — only depends on OneBCG.ToolEngine.Sdk
using OneBCG.ToolEngine.Sdk.Base;
using OneBCG.ToolEngine.Sdk.Attributes;

[Tool("finance", "calculate-irr", "1.0.0",
    Description = "Calculates internal rate of return for a series of cash flows",
    Risk = ApprovalRisk.Low)]
public sealed class CalculateIrrTool : LogicToolBase<IrrInput, IrrOutput>
{
    protected override Task<Result<IrrOutput>> ExecuteLogicAsync(
        IrrInput input, CancellationToken ct = default)
    {
        var irr = FinanceMath.CalculateIrr(input.CashFlows);
        return Task.FromResult(Result.Success(new IrrOutput(irr)));
    }
}
```

### SDK publish pipeline — `.github/workflows/publish-sdk.yml`

```yaml
name: Publish SDK
on:
  push:
    tags: ['sdk/v*']

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - name: Pack
        run: |
          VERSION=${GITHUB_REF#refs/tags/sdk/v}
          dotnet pack src/Sdk/OneBCG.ToolEngine.Sdk \
            -c Release /p:VersionPrefix=$VERSION \
            -o ./artifacts
      - name: Push to GitHub Packages
        run: |
          dotnet nuget push ./artifacts/*.nupkg \
            --api-key ${{ secrets.GITHUB_TOKEN }} \
            --source https://nuget.pkg.github.com/onebcg/index.json
```

---

## A4.2 — TypeScript + Python Client SDK Generation

### Why

The React frontend and data science teams write manual `fetch` wrappers today.
When the API changes, clients break silently at runtime. Auto-generated clients
from OpenAPI ensure compile-time contract enforcement and always stay in sync.

### NSwag for TypeScript — `nswag.json` in `frontend/`

```json
{
  "runtime": "Net80",
  "documentGenerator": {
    "fromDocument": {
      "url": "https://localhost:7001/swagger/v1/swagger.json"
    }
  },
  "codeGenerators": {
    "openApiToTypeScriptClient": {
      "className": "ToolEngineClient",
      "template": "Fetch",
      "generateClientInterfaces": true,
      "useTransformOptionsMethod": true,
      "useSingletonProvider": false,
      "httpClientType": "fetch",
      "outputFilePath": "src/generated/ToolEngineClient.ts"
    }
  }
}
```

### Run generation in CI

```yaml
- name: Generate TypeScript client
  run: |
    dotnet tool restore
    dotnet nswag run frontend/nswag.json
    # Fail build if client changed without committing
    git diff --exit-code frontend/src/generated/
```

### Kiota for Python — `kiota-config.json`

```json
{
  "version": "1.0",
  "clients": {
    "toolengine-python": {
      "descriptionLocation": "https://localhost:7001/swagger/v1/swagger.json",
      "language": "python",
      "outputPath": "./sdk/python/toolengine_client",
      "clientClassName": "ToolEngineClient",
      "namespaceName": "toolengine_client"
    }
  }
}
```

### Generated client usage — TypeScript

```typescript
// Generated client — type-safe, always in sync with API
import { ToolEngineClient } from './generated/ToolEngineClient';

const client = new ToolEngineClient('https://api.onebcg.com', {
  fetch: (url, init) =>
    fetch(url, { ...init, headers: { ...init?.headers, Authorization: `Bearer ${token}` } })
});

// Compile error if API contract changes
const result = await client.tools.math.calculate.v1.invoke.post({
  input: { expression: "2+2" }
});
```

---

## A4.3 — Semantic Versioning + Breaking Change Detection

### Why

Tool versions are currently free-form strings with no enforcement. A team
can release `v2` that removes a required input field — silently breaking
every agent that calls the tool. Semantic versioning + schema diff detection
prevents silent breaking changes.

### Version validation — `ToolEngine.Tools.Registry/Versioning/SemanticVersion.cs`

```csharp
namespace ToolEngine.Tools.Registry.Versioning;

public readonly record struct SemanticVersion(int Major, int Minor, int Patch)
    : IComparable<SemanticVersion>
{
    public static SemanticVersion Parse(string version)
    {
        var parts = version.TrimStart('v').Split('.');
        if (parts.Length != 3 || !parts.All(p => int.TryParse(p, out _)))
            throw new ArgumentException($"Invalid semantic version: '{version}'. Expected format: MAJOR.MINOR.PATCH");

        return new(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    public bool IsBreakingFrom(SemanticVersion previous) => Major > previous.Major;
    public bool IsFeatureFrom(SemanticVersion previous)  => Minor > previous.Minor;
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
    public int CompareTo(SemanticVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) :
        Minor != other.Minor ? Minor.CompareTo(other.Minor) :
        Patch.CompareTo(other.Patch);
}
```

### Schema diff detector — `ToolEngine.Tools.Registry/Versioning/SchemaBreakingChangeDetector.cs`

```csharp
namespace ToolEngine.Tools.Registry.Versioning;

public sealed class SchemaBreakingChangeDetector
{
    // Returns list of breaking changes between two JSON Schema versions
    public IReadOnlyList<BreakingChange> Detect(
        JsonElement previousSchema, JsonElement newSchema)
    {
        var changes = new List<BreakingChange>();

        // Check for removed required fields
        var prevRequired = GetRequiredFields(previousSchema);
        var newRequired  = GetRequiredFields(newSchema);
        var newProps     = GetPropertyNames(newSchema);

        foreach (var field in prevRequired)
        {
            if (!newProps.Contains(field))
                changes.Add(new BreakingChange(
                    BreakingChangeType.RequiredFieldRemoved,
                    $"Required field '{field}' was removed from input schema"));
        }

        // Check for type changes on existing fields
        var prevProps = GetProperties(previousSchema);
        var currProps = GetProperties(newSchema);

        foreach (var (name, prevType) in prevProps)
        {
            if (currProps.TryGetValue(name, out var newType) && prevType != newType)
                changes.Add(new BreakingChange(
                    BreakingChangeType.FieldTypeChanged,
                    $"Field '{name}' type changed from '{prevType}' to '{newType}'"));
        }

        return changes;
    }
}

public sealed record BreakingChange(BreakingChangeType Type, string Description);
public enum BreakingChangeType
{
    RequiredFieldRemoved,
    FieldTypeChanged,
    OutputFieldRemoved,
    VersionDowngrade
}
```

### Enforce in tool registration

```csharp
// In ToolRegistry.Register():
if (existing is not null)
{
    var currentVersion  = SemanticVersion.Parse(tool.Version);
    var previousVersion = SemanticVersion.Parse(existing.Version);

    if (currentVersion < previousVersion)
        throw new InvalidOperationException(
            $"Tool {tool.FullName}: version downgrade from {previousVersion} to {currentVersion} is not allowed.");

    if (currentVersion.IsBreakingFrom(previousVersion))
    {
        var changes = _breakingChangeDetector.Detect(
            existing.InputSchema, tool.InputSchema);

        if (changes.Any())
            _logger.LogWarning(
                "Tool {FullName} v{Version} has {Count} breaking changes: {Changes}",
                tool.FullName, tool.Version, changes.Count,
                string.Join("; ", changes.Select(c => c.Description)));
    }
}
```

---

## A4.4 — Tool Marketplace Catalog

### Why

Tools are currently discovered only through Swagger. A purpose-built catalog
with search, tags, usage stats, and approval-risk indicators dramatically
reduces time for developers to find and adopt the right tool.

### Catalog API endpoint — `ToolEngine.Api/Endpoints/CatalogEndpoints.cs`

```csharp
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /catalog?q=weather&tag=external-api&risk=low
        app.MapGet("/catalog", async (
            [FromQuery] string? q,
            [FromQuery] string? tag,
            [FromQuery] string? risk,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            IToolRegistry registry,
            ReadDbContext db,
            ICurrentUser user,
            CancellationToken ct) =>
        {
            var tools = registry.GetAll(user.TenantId!)
                .Where(t => q is null ||
                    t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    t.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(t => tag is null ||
                    t.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .Where(t => risk is null ||
                    t.ApprovalRisk.ToString().Equals(risk, StringComparison.OrdinalIgnoreCase));

            var toolNames    = tools.Select(t => t.FullName).ToHashSet();
            var usageStats   = await GetUsageStatsAsync(toolNames, db, ct);

            var catalogItems = tools
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new CatalogItem(
                    FullName:    t.FullName,
                    Namespace:   t.Namespace,
                    Name:        t.Name,
                    Version:     t.Version,
                    Description: t.Description,
                    Risk:        t.ApprovalRisk,
                    Tags:        t.Tags,
                    InputSchema: t.InputSchema,
                    Stats:       usageStats.GetValueOrDefault(t.FullName)))
                .ToList();

            return Results.Ok(new PagedResult<CatalogItem>(
                catalogItems, tools.Count(), page, pageSize));
        })
        .RequireAuthorization();
    }
}
```

### Catalog React component structure — `frontend/src/components/Catalog/`

```
Catalog/
  CatalogPage.tsx           — search bar, filter chips, grid layout
  CatalogCard.tsx           — tool card: name, description, risk badge, usage count
  CatalogDetailDrawer.tsx   — slide-in panel: full schema, examples, try-it form
  useToolSearch.ts          — debounced search hook with pagination
  RiskBadge.tsx             — colour-coded ApprovalRisk indicator
```

---

## A4.5 — Canary Deployment for Tool Versions

### Why

A tool update that introduces a subtle bug causes hard-to-diagnose LLM behaviour
regressions. Canary routing sends a small percentage of traffic to the new version,
monitors the error rate, and promotes or rolls back automatically.

### Canary config in `ToolRegistry`

```csharp
public sealed class CanaryConfig
{
    public string   TenantId       { get; init; } = "*";  // "*" = all tenants
    public string   ToolFullName   { get; init; } = default!;
    public string   CanaryVersion  { get; init; } = default!;
    public string   StableVersion  { get; init; } = default!;
    public int      CanaryPercent  { get; init; }         // 0–100
    public DateTime PromoteAfterUtc { get; init; }
    public double   MaxErrorRate   { get; init; } = 0.05; // auto-rollback threshold
}
```

### Canary routing in `ToolRegistry.Resolve`

```csharp
public ITool? Resolve(string ns, string name, string version, string tenantId)
{
    if (version == "latest")
    {
        // Check if canary is configured for this tool + tenant
        var canary = _canaryConfigs
            .FirstOrDefault(c =>
                c.ToolFullName == $"{ns}.{name}" &&
                (c.TenantId == "*" || c.TenantId == tenantId));

        if (canary is not null)
        {
            // Deterministic routing: hash(tenantId + correlationId) % 100
            var hash    = Math.Abs(HashCode.Combine(tenantId, _correlationId)) % 100;
            var version = hash < canary.CanaryPercent
                ? canary.CanaryVersion
                : canary.StableVersion;

            return GetTool(ns, name, version, tenantId);
        }
    }
    return GetTool(ns, name, version, tenantId);
}
```

### Canary promotion background service

```csharp
// Runs every 5 minutes — checks canary error rate and promotes or rolls back
public sealed class CanaryPromotionService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var canary in _registry.GetActiveCanaries())
            {
                var errorRate = await _metricsReader.GetErrorRateAsync(
                    canary.ToolFullName, canary.CanaryVersion,
                    window: TimeSpan.FromMinutes(30), ct);

                if (errorRate > canary.MaxErrorRate)
                {
                    _logger.LogWarning("Canary rollback: {Tool} v{Version} error rate {Rate:P}",
                        canary.ToolFullName, canary.CanaryVersion, errorRate);
                    _registry.RollbackCanary(canary.ToolFullName);
                }
                else if (DateTime.UtcNow >= canary.PromoteAfterUtc)
                {
                    _logger.LogInformation("Canary promoted: {Tool} v{Version}",
                        canary.ToolFullName, canary.CanaryVersion);
                    _registry.PromoteCanary(canary.ToolFullName);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

---

## A4.6 — AsyncAPI Spec Generation

### Why

OpenAPI documents the synchronous REST surface. The event-driven surface
(domain events from Phase A2) has no published contract today. AsyncAPI is
the standard for documenting event channels — consumers of the event bus
need this to build correct subscribers.

### AsyncAPI document — `docs/asyncapi.yaml` (generated or hand-authored)

```yaml
asyncapi: 3.0.0
info:
  title: ToolEngine Event Bus
  version: 2026.1.0
  description: Domain events emitted by ToolEngine v2026

defaultContentType: application/json

channels:
  toolInvoked:
    address: toolengine.tool-invoked
    description: Emitted immediately when a tool invocation is accepted
    messages:
      ToolInvokedMessage:
        $ref: '#/components/messages/ToolInvokedEvent'

  toolCompleted:
    address: toolengine.tool-completed
    messages:
      ToolCompletedMessage:
        $ref: '#/components/messages/ToolCompletedEvent'

  approvalRequested:
    address: toolengine.approval-requested
    messages:
      ApprovalRequestedMessage:
        $ref: '#/components/messages/ApprovalRequestedEvent'

components:
  messages:
    ToolInvokedEvent:
      payload:
        type: object
        required: [correlationId, tenantId, toolFullName, toolVersion, callerType, occurredAt]
        properties:
          correlationId: { type: string, format: uuid }
          tenantId:      { type: string }
          toolFullName:  { type: string, example: "math.calculate" }
          toolVersion:   { type: string, example: "1.0.0" }
          callerType:    { type: string, enum: [Human, AiAgent, SystemService] }
          occurredAt:    { type: string, format: date-time }
```

### AsyncAPI generator script — `scripts/generate-asyncapi.sh`

```bash
#!/bin/bash
# Generate AsyncAPI HTML documentation from spec
npx @asyncapi/generator \
  docs/asyncapi.yaml \
  @asyncapi/html-template \
  -o docs/asyncapi-html \
  --force-write
```

---

## A4.7 — CLI Scaffolding

### Why

Setting up a new tool project today requires: reading SKILL files, copying
boilerplate, adding NuGet references, wiring DI, and writing test scaffolding.
This takes 30–60 minutes and is error-prone. The CLI reduces this to 2 minutes
with a single command.

### CLI project — `src/Cli/ToolEngine.Cli/Commands/NewToolCommand.cs`

```csharp
[Command("tool new")]
public sealed class NewToolCommand : AsyncCommand<NewToolSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext ctx, NewToolSettings settings)
    {
        AnsiConsole.MarkupLine("[bold green]ONE BCG ToolEngine — New Tool Scaffold[/]");

        var ns      = settings.Namespace   ?? AnsiConsole.Ask<string>("Tool namespace (e.g. [blue]finance[/]):");
        var name    = settings.Name        ?? AnsiConsole.Ask<string>("Tool name (e.g. [blue]calculate-irr[/]):");
        var version = settings.Version     ?? "1.0.0";
        var risk    = settings.Risk        ?? AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Approval risk level:")
                .AddChoices("Low", "Medium", "High", "Critical"));

        var outputDir = Path.Combine(settings.Output ?? ".", $"{ns}.{name}");
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(Path.Combine(outputDir, "src"));
        Directory.CreateDirectory(Path.Combine(outputDir, "tests"));

        // Scaffold tool project from embedded template
        await ScaffoldFileAsync(outputDir, "src", $"{ToPascalCase(name)}Tool.cs",
            Templates.ToolClass(ns, name, version, risk));
        await ScaffoldFileAsync(outputDir, "src", $"{ToPascalCase(name)}Input.cs",
            Templates.InputClass(ns, name));
        await ScaffoldFileAsync(outputDir, "src", $"{ToPascalCase(name)}Output.cs",
            Templates.OutputClass(ns, name));
        await ScaffoldFileAsync(outputDir, "tests", $"{ToPascalCase(name)}ToolTests.cs",
            Templates.TestClass(ns, name));
        await ScaffoldFileAsync(outputDir, "", $"{ns}.{name}.csproj",
            Templates.CsprojFile(ns, name));

        AnsiConsole.MarkupLine($"[green]Scaffolded:[/] {outputDir}");
        AnsiConsole.MarkupLine("Next: [blue]cd {0} && dotnet build[/]", outputDir);
        return 0;
    }
}
```

### Scaffold templates — `ToolEngine.Cli/Templates/`

```csharp
public static class Templates
{
    public static string ToolClass(string ns, string name, string version, string risk) =>
        $$"""
        using OneBCG.ToolEngine.Sdk.Base;
        using OneBCG.ToolEngine.Sdk.Attributes;
        using OneBCG.ToolEngine.Sdk.Domain;

        namespace {{ToPascalCase(ns)}}.{{ToPascalCase(name)}};

        [Tool("{{ns}}", "{{name}}", "{{version}}",
            Description = "TODO: describe this tool",
            Risk = ApprovalRisk.{{risk}})]
        public sealed class {{ToPascalCase(name)}}Tool
            : LogicToolBase<{{ToPascalCase(name)}}Input, {{ToPascalCase(name)}}Output>
        {
            protected override Task<Result<{{ToPascalCase(name)}}Output>> ExecuteLogicAsync(
                {{ToPascalCase(name)}}Input input, CancellationToken ct = default)
            {
                // TODO: implement
                throw new NotImplementedException();
            }
        }
        """;
}
```

### CLI usage

```bash
# Install globally
dotnet tool install -g OneBCG.ToolEngine.Cli

# Scaffold a new tool
toolengine tool new --namespace finance --name calculate-irr --risk Low

# Run tool locally against dev registry
toolengine tool test finance.calculate-irr '{"cashFlows": [-100, 50, 60, 70]}'

# Publish tool to registry
toolengine tool publish --env staging
```

---

## Phase A4 Completion Checklist

### A4.1 — NuGet SDK
- [ ] `OneBCG.ToolEngine.Sdk` project created in `src/Sdk/`
- [ ] SDK contains: `ITool`, base classes, `Result<T>`, `ToolAttribute`, `ToolInputValidator`
- [ ] SDK has ZERO dependency on `ToolEngine.Infrastructure` or `ToolEngine.Application`
- [ ] `PackageId`, `Authors`, `Description`, `PackageLicenseExpression` set in `.csproj`
- [ ] GitHub Actions workflow publishes on `sdk/v*` tag
- [ ] External sample tool project compiles using only the SDK NuGet

### A4.2 — SDK Generation
- [ ] `nswag.json` configured in `frontend/` — outputs to `src/generated/`
- [ ] CI step generates TypeScript client and fails if uncommitted changes detected
- [ ] `kiota-config.json` configured for Python client output
- [ ] Generated clients are NOT manually edited — CI enforces this
- [ ] OpenAPI spec is generated from the running app, not hand-authored

### A4.3 — Versioning
- [ ] `SemanticVersion.Parse` validates `MAJOR.MINOR.PATCH` format strictly
- [ ] Version downgrade throws `InvalidOperationException` on registration
- [ ] `SchemaBreakingChangeDetector` checks removed required fields + type changes
- [ ] Breaking changes logged as `LogWarning` with field-level details
- [ ] `SemanticVersion.IsBreakingFrom` returns true when `Major > previous.Major`

### A4.4 — Marketplace
- [ ] `GET /catalog` endpoint with `q`, `tag`, `risk`, `page`, `pageSize` query params
- [ ] `CatalogItem` DTO includes: usage stats, full schema, risk, tags
- [ ] `CatalogPage.tsx` component with search + filter + pagination
- [ ] `RiskBadge.tsx` colour-codes ApprovalRisk (Low=green, Medium=yellow, High=orange, Critical=red)

### A4.5 — Canary Deployment
- [ ] `CanaryConfig` entity: tool, tenantId, canaryVersion, stableVersion, canaryPercent, maxErrorRate
- [ ] `ToolRegistry.Resolve` performs deterministic percent-based routing
- [ ] `CanaryPromotionService` checks error rate every 5 minutes
- [ ] Auto-rollback when `errorRate > maxErrorRate`
- [ ] Auto-promote when `DateTime.UtcNow >= PromoteAfterUtc`

### A4.6 — AsyncAPI
- [ ] `docs/asyncapi.yaml` documents all domain events from Phase A2
- [ ] Each event channel has: address, description, message schema
- [ ] `generate-asyncapi.sh` script produces HTML docs
- [ ] AsyncAPI version matches event schema versions

### A4.7 — CLI
- [ ] `toolengine tool new` scaffolds: tool class, input, output, tests, csproj
- [ ] Scaffold uses `LogicToolBase` by default (user prompted for type)
- [ ] `toolengine tool test` invokes tool against local dev registry
- [ ] CLI installed via `dotnet tool install -g OneBCG.ToolEngine.Cli`

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
