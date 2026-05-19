---
name: toolengine-unit-testing
description: >
  Defines the mandatory unit and integration testing standards for all ONE BCG
  application development. Covers: testing pyramid ratios, Given/When/Then
  naming convention, Builder pattern for test objects, FluentAssertions usage,
  Moq strict-mock guidelines, test isolation rules, coverage requirements
  (80% line / 70% branch), mutation testing with Stryker.NET, and contract
  test patterns for tool schemas. Apply this SKILL to every new project and
  every PR that adds or changes behaviour.
classification: Confidential - Internal Use Only
---

# Unit Testing Standards — ONE BCG Development Platform

## Testing Philosophy

Tests are first-class production code. They have the same naming standards,
the same review requirements, and the same quality bar as the code they test.
A failing test is a blocker — never skip or comment out a test to make CI pass.

---

## 1. Testing Pyramid

```
         ┌─────────────────────┐
         │   E2E / Contract    │   5% — slow, expensive, environment-dependent
         │   (Playwright, WireMock) │
         ├─────────────────────┤
         │   Integration Tests │  20% — EF Core, real DB, MediatR pipeline
         │   (TestContainers)  │
         ├─────────────────────┤
         │      Unit Tests     │  75% — fast, isolated, no I/O
         │   (xUnit + Moq)     │
         └─────────────────────┘
```

Unit tests run on every commit (< 30 seconds).
Integration tests run on every PR (< 5 minutes).
E2E tests run on main branch before production deploy.

---

## 2. Project Structure

```
tests/
  ToolEngine.Application.Tests/         — MediatR behavior unit tests
  ToolEngine.Domain.Tests/              — Domain entity unit tests
  ToolEngine.Infrastructure.Tests/      — Repository / EF Core integration tests
  ToolEngine.Api.Tests/                 — API endpoint integration tests
  ToolEngine.Tools.Tests/               — Tool handler unit tests
  ToolEngine.RedTeam.Tests/             — AI red-team tests (Phase A5.4)
```

### Test project `.csproj` template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit"                          Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio"      Version="2.*" />
    <PackageReference Include="FluentAssertions"               Version="6.*" />
    <PackageReference Include="Moq"                            Version="4.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk"         Version="17.*" />
    <PackageReference Include="coverlet.collector"             Version="6.*" />
    <PackageReference Include="Testcontainers.PostgreSql"      Version="3.*" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.*" />
  </ItemGroup>
</Project>
```

---

## 3. Naming Convention — Given / When / Then

Every test method name must follow: `Given{Context}_When{Action}_Then{Expectation}`

```csharp
// CORRECT
[Fact]
public async Task GivenActiveTenant_WhenNamespaceIsAllowed_ThenCommandSucceeds()

[Fact]
public async Task GivenInactiveTenant_WhenCommandIsSent_ThenReturnsUnauthorized()

[Theory]
[InlineData("")]
[InlineData(null)]
[InlineData("   ")]
public void GivenEmptyTenantId_WhenTenantIsCreated_ThenReturnsValidationError(string? id)

// BANNED — vague, doesn't describe the scenario
[Fact]
public void Test1()
public void TenantTest()
public void ItShouldWork()
```

### Test class naming

One test class per production class, suffixed with `Tests`:

```
TenantAuthorizationBehavior.cs  →  TenantAuthorizationBehaviorTests.cs
ApprovalBehavior.cs             →  ApprovalBehaviorTests.cs
Tenant.cs                       →  TenantTests.cs
```

---

## 4. Builder Pattern for Test Objects

Use the Builder pattern to construct test objects. Never construct entities
directly with `new` in test code — builders centralise setup and make
tests resilient to constructor changes.

### Builder base — `Tests/Builders/BuilderBase.cs`

```csharp
namespace ToolEngine.Application.Tests.Builders;

public abstract class BuilderBase<TEntity, TBuilder>
    where TBuilder : BuilderBase<TEntity, TBuilder>
{
    protected abstract TEntity Build();

    public static implicit operator TEntity(BuilderBase<TEntity, TBuilder> builder)
        => builder.Build();
}
```

### Tenant builder example

```csharp
namespace ToolEngine.Application.Tests.Builders;

public sealed class TenantBuilder : BuilderBase<Tenant, TenantBuilder>
{
    private string _id             = "test-tenant";
    private string _name           = "Test Tenant";
    private bool   _isActive       = true;
    private int    _dailyBudget    = 1000;
    private int    _maxTokens      = 4096;
    private readonly List<string> _namespaces = new() { "*" };

    public TenantBuilder WithId(string id)            { _id = id; return this; }
    public TenantBuilder WithName(string name)         { _name = name; return this; }
    public TenantBuilder Inactive()                    { _isActive = false; return this; }
    public TenantBuilder WithDailyBudget(int budget)   { _dailyBudget = budget; return this; }
    public TenantBuilder WithNamespace(string ns)
    {
        _namespaces.Clear();
        _namespaces.Add(ns);
        return this;
    }
    public TenantBuilder WithNoNamespaces()
    {
        _namespaces.Clear();
        return this;
    }

    protected override Tenant Build()
    {
        var clock  = new FakeDateTimeProvider(DateTimeOffset.UtcNow);
        var result = Tenant.Create(_id, _name, "test-user", clock);
        var tenant = result.Value;

        if (!_isActive) tenant.Deactivate();
        tenant.SetLimits(_maxTokens, _dailyBudget);
        foreach (var ns in _namespaces) tenant.AllowNamespace(ns);

        return tenant;
    }
}

// Usage in tests — clean, intent-revealing
var activeTenant   = new TenantBuilder().Build();
var inactiveTenant = new TenantBuilder().Inactive().Build();
var restrictedTenant = new TenantBuilder()
    .WithNamespace("finance")
    .Build();
```

### Command builder example

```csharp
public sealed class ExecuteToolCommandBuilder
{
    private Guid   _correlationId = Guid.NewGuid();
    private string _tenantId      = "test-tenant";
    private string _namespace     = "math";
    private string _name          = "calculate";
    private string _version       = "1.0.0";
    private string _inputJson     = "{}";
    private CallerType _callerType = CallerType.Human;

    public ExecuteToolCommandBuilder ForTenant(string tenantId)    { _tenantId = tenantId; return this; }
    public ExecuteToolCommandBuilder ForTool(string ns, string name) { _namespace = ns; _name = name; return this; }
    public ExecuteToolCommandBuilder WithInput(object input)       { _inputJson = JsonSerializer.Serialize(input); return this; }
    public ExecuteToolCommandBuilder AsAgent()                     { _callerType = CallerType.AiAgent; return this; }

    public ExecuteToolCommandJson Build() => new(
        CorrelationId: _correlationId,
        TenantId:      _tenantId,
        Namespace:     _namespace,
        Name:          _name,
        Version:       _version,
        InputJson:     _inputJson,
        CallerType:    _callerType);
}
```

---

## 5. FluentAssertions Guidelines

Use FluentAssertions for all assertions. Never use `Assert.Equal` directly —
FluentAssertions produces significantly better failure messages.

```csharp
// CORRECT — FluentAssertions
result.IsSuccess.Should().BeTrue();
result.Value.Should().NotBeNull();
result.Value.TenantId.Should().Be("test-tenant");

tenant.AllowedNamespaces.Should().ContainSingle()
    .Which.Should().Be("finance");

response.StatusCode.Should().Be(HttpStatusCode.Accepted);

// Async assertions
Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);
await act.Should().ThrowAsync<InvalidOperationException>()
    .WithMessage("*not found*");

// Exception assertions for Result<T>
result.IsFailure.Should().BeTrue();
result.Error.Code.Should().Be("VALIDATION_ERROR");
result.Error.Description.Should().Contain("TenantId");

// Collection assertions
tenants.Should().HaveCount(3);
tenants.Should().AllSatisfy(t => t.IsActive.Should().BeTrue());
tenants.Should().BeInAscendingOrder(t => t.Name);

// BANNED — raw xUnit assertions
Assert.True(result.IsSuccess);
Assert.Equal("test-tenant", result.Value.TenantId);
```

---

## 6. Moq — Strict Mock Guidelines

```csharp
// RULE 1: Always use strict mocks in unit tests
// MockBehavior.Strict throws on any unexpected call — catches over-calling
var mockRepo = new Mock<IReadRepository<Tenant, string>>(MockBehavior.Strict);

// RULE 2: Setup only what the test needs — nothing more
mockRepo.Setup(r => r.GetByIdAsync("test-tenant", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TenantBuilder().Build());

// RULE 3: Verify interactions when the test is about side effects
// (not just return values)
mockRepo.Verify(r => r.GetByIdAsync("test-tenant", It.IsAny<CancellationToken>()),
    Times.Once);

// RULE 4: Use It.IsAny<CancellationToken>() for all CT parameters
// Never match on a specific CancellationToken value

// RULE 5: Never mock what you own
// Mock interfaces you depend on; never mock your own domain classes
// Bad:  var mockTenant = new Mock<Tenant>();
// Good: var tenant     = new TenantBuilder().Build();

// RULE 6: Never mock static methods — refactor instead
// If the code under test calls a static method, extract it behind an interface

// RULE 7: MockBehavior.Loose is permitted only in integration tests
// where setting up every call would make the test unreadable
```

---

## 7. MediatR Behavior Tests

Behavior tests follow a consistent structure:

```csharp
public sealed class TenantAuthorizationBehaviorTests
{
    // Arrange shared dependencies
    private readonly Mock<IReadRepository<Tenant, string>> _tenantRepo;
    private readonly TenantAuthorizationBehavior<ExecuteToolCommandJson, ToolResponse<object>> _sut;

    public TenantAuthorizationBehaviorTests()
    {
        _tenantRepo = new Mock<IReadRepository<Tenant, string>>(MockBehavior.Strict);
        _sut = new TenantAuthorizationBehavior<ExecuteToolCommandJson, ToolResponse<object>>(
            _tenantRepo.Object);
    }

    [Fact]
    public async Task GivenActiveTenantWithAllowedNamespace_WhenHandled_ThenCallsNext()
    {
        // Arrange
        var tenant  = new TenantBuilder().WithNamespace("math").Build();
        var command = new ExecuteToolCommandBuilder().ForTenant("test-tenant").Build();
        var called  = false;

        _tenantRepo
            .Setup(r => r.GetByIdAsync("test-tenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        RequestHandlerDelegate<ToolResponse<object>> next = () =>
        {
            called = true;
            return Task.FromResult(ToolResponse<object>.Ok(command.CorrelationId, new()));
        };

        // Act
        var result = await _sut.Handle(command, next, CancellationToken.None);

        // Assert
        called.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        _tenantRepo.VerifyAll();
    }

    [Fact]
    public async Task GivenInactiveTenant_WhenHandled_ThenReturnsUnauthorized()
    {
        // Arrange
        var tenant  = new TenantBuilder().Inactive().Build();
        var command = new ExecuteToolCommandBuilder().Build();

        _tenantRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        // Act
        var result = await _sut.Handle(command, () => throw new Exception("Should not be called"),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("UNAUTHORIZED");
    }
}
```

---

## 8. Integration Testing with TestContainers

Use TestContainers for tests that need a real database. Never mock EF Core.

```csharp
// Shared fixture — one PostgreSQL container per test collection
[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("toolengine_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Apply migrations
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var db = new AppDbContext(opts, /* ... */);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

// Test using real database
[Collection("Database")]
public sealed class TenantRepositoryTests
{
    private readonly AppDbContext _db;

    public TenantRepositoryTests(DatabaseFixture fixture)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        _db = new AppDbContext(opts, /* ... */);
    }

    [Fact]
    public async Task GivenTenantExists_WhenGetById_ThenReturnsTenant()
    {
        // Arrange
        var tenant = new TenantBuilder().WithId("integration-test").Build();
        await _db.Set<Tenant>().AddAsync(tenant);
        await _db.SaveChangesAsync();

        var repo = new CachedTenantReadRepository(
            new ReadRepository<Tenant, string>(_db));

        // Act
        var result = await repo.GetByIdAsync("integration-test");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("integration-test");
    }
}
```

---

## 9. API Endpoint Tests

```csharp
public sealed class ToolEndpointsTests : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client;

    public ToolEndpointsTests(ApiTestFixture fixture) =>
        _client = fixture.CreateAuthenticatedClient("test-tenant");

    [Fact]
    public async Task GivenValidInput_WhenInvokeTool_ThenReturns200WithResult()
    {
        // Arrange
        var payload = new { expression = "2+2" };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/tools/math/calculate/v1.0.0/invoke", payload);

        // Assert
        response.Should().HaveStatusCode(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ToolResponse<object>>();
        body!.Success.Should().BeTrue();
        body.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenHighRiskTool_WhenInvoked_ThenReturns202WithApprovalLocation()
    {
        var response = await _client.PostAsJsonAsync(
            "/tools/hr/delete-employee/v1.0.0/invoke",
            new { employeeId = "E001" });

        response.Should().HaveStatusCode(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Should().ContainKey("Retry-After");
    }
}
```

---

## 10. Coverage Requirements

| Coverage type | Minimum gate | Enforced in |
|---------------|-------------|-------------|
| Line coverage | 80% | CI quality gate |
| Branch coverage | 70% | CI quality gate |
| New code coverage | 80% | SonarCloud new-code gate |

### Coverage collection command

```bash
dotnet test --collect:"XPlat Code Coverage" \
            --results-directory ./coverage \
            --settings tests/coverage.runsettings

# Generate HTML report
dotnet tool run reportgenerator \
  -reports:"coverage/**/coverage.cobertura.xml" \
  -targetdir:coverage/html \
  -reporttypes:Html
```

### `coverage.runsettings`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Exclude>
            <!-- Exclude generated, migration, and scaffolded code -->
            [*]*.Migrations.*
            [*]*.Generated.*
            [*]*Program*
          </Exclude>
          <ExcludeByAttribute>
            ExcludeFromCodeCoverage
            GeneratedCodeAttribute
          </ExcludeByAttribute>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

---

## 11. Mutation Testing with Stryker.NET

Mutation testing validates test quality: if a test passes even when the
production code is mutated (a condition flipped, an operator changed),
the test is not actually catching the behaviour it claims to test.

### Setup — `stryker-config.json`

```json
{
  "stryker-config": {
    "project":          "ToolEngine.Application",
    "test-projects":    ["tests/ToolEngine.Application.Tests"],
    "reporters":        ["html", "progress", "json"],
    "mutation-level":   "Advanced",
    "thresholds": {
      "high":    85,
      "low":     75,
      "break":   70
    },
    "ignore-mutations": [
      "string",     // string literal changes rarely matter
      "Linq"        // LINQ operator mutations produce too many survivors
    ],
    "target-framework": "net8.0"
  }
}
```

### Run mutation tests

```bash
# Install Stryker
dotnet tool install -g dotnet-stryker

# Run — generates HTML report in StrykerOutput/
dotnet stryker

# CI: break build if mutation score < threshold
dotnet stryker --break-at 70
```

### Minimum mutation score: **70%** for behavior and domain layers.

---

## 12. Fake Implementations vs. Mocks

Use hand-written fake implementations for infrastructure interfaces that
are called multiple times across many tests. Fakes are more reliable than
Moq setups that grow stale.

```csharp
// FakeDateTimeProvider — used in 30+ tests; cleaner than 30 Mock setups
public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; private set; }

    public FakeDateTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

// FakeCacheProvider — deterministic in-memory cache for unit tests
public sealed class FakeCacheProvider : ICacheProvider
{
    private readonly Dictionary<string, (object value, DateTimeOffset? expiry)> _store = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var entry) &&
            (entry.expiry is null || entry.expiry > DateTimeOffset.UtcNow))
            return Task.FromResult((T?)entry.value);
        return Task.FromResult(default(T?));
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        _store[key] = (value!, expiry.HasValue ? DateTimeOffset.UtcNow + expiry : null);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }

    public Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var current = _store.TryGetValue(key, out var e) ? (long)e.value : 0L;
        var next    = current + delta;
        _store[key] = (next, expiry.HasValue ? DateTimeOffset.UtcNow + expiry : null);
        return Task.FromResult(next);
    }
}
```

---

## Phase Completion Checklist

- [ ] Test project per production project created (unit + integration split)
- [ ] All test methods named `Given_When_Then`
- [ ] Builder classes for: Tenant, ExecuteToolCommand, PendingApproval
- [ ] FluentAssertions used throughout — zero raw `Assert.*` calls
- [ ] Strict mocks (`MockBehavior.Strict`) in all unit tests
- [ ] `FakeDateTimeProvider` and `FakeCacheProvider` available in `Builders/`
- [ ] Integration tests use TestContainers PostgreSQL — not SQLite, not mocks
- [ ] API tests use `WebApplicationFactory` with real auth middleware
- [ ] Coverage gate: 80% line, 70% branch enforced in CI
- [ ] `stryker-config.json` with `break: 70` mutation threshold
- [ ] All behaviors (TenantAuth, Approval, Budget, LoopDetection, Audit) have test class
- [ ] `MockBehavior.Loose` justified by a comment when used in integration tests

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
