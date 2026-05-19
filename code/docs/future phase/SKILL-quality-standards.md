---
name: toolengine-quality-standards
description: >
  Defines the mandatory code quality standards for all ONE BCG application
  development. Covers: SOLID, DRY, YAGNI and Clean Code principles enforced
  as reviewable gates, naming conventions for all .NET and TypeScript
  constructs, cyclomatic complexity ceiling of 10, mandatory code review
  checklist, null safety and defensive programming guidelines, and performance
  anti-pattern prevention rules. Apply this SKILL to every new service and
  every PR that touches existing code.
classification: Confidential - Internal Use Only
---

# Quality Standards — ONE BCG Development Platform

## Purpose

This document defines non-negotiable quality standards for all code produced
under the ONE BCG technology platform. These are not style preferences — they
are enforced gates. Code that violates these standards is not merged.

---

## 1. Core Design Principles

### SOLID

| Principle | Rule | Violation signal |
|-----------|------|-----------------|
| **Single Responsibility** | One class = one reason to change | Class name contains "And", "Manager", "Helper", "Utils" |
| **Open/Closed** | Extend via abstraction; never modify existing behaviour | `if (type == "X")` switch on type names |
| **Liskov Substitution** | Subclass must honour the contract of its base | Overridden method throws `NotSupportedException` |
| **Interface Segregation** | No interface method a client doesn't use | Interface with 10+ methods consumed by a class that implements 3 |
| **Dependency Inversion** | Depend on abstractions, never on concrete infrastructure | `new SqlRepository()` inside a handler |

### DRY (Don't Repeat Yourself)

- Three identical code blocks → extract a shared method or base class.
- Three identical configurations → extract a shared config section.
- Exception: tests are allowed to be explicit over DRY. Test readability beats brevity.

### YAGNI (You Aren't Gonna Need It)

- Do not design for hypothetical future requirements.
- Do not add abstraction layers before they have two concrete implementations.
- Do not implement features that are not in the current sprint acceptance criteria.

### Clean Code Rules

```
Rule 1 — Names tell the truth.
  Bad:  int d;  void process();  class DataHelper
  Good: int daysUntilExpiry;  void SubmitApprovalRequest();  class TenantCostAggregator

Rule 2 — Functions do one thing.
  Max 20 lines per method. If it needs a comment to explain what a section does,
  extract that section into a named method.

Rule 3 — Comments explain WHY, never WHAT.
  The code explains what. The comment explains the hidden constraint, the
  compliance reason, or the subtle invariant.

Rule 4 — No magic values.
  Bad:  if (retryCount > 5)
  Good: if (retryCount > MaxRetryAttempts)  // or const int MaxRetryAttempts = 5;

Rule 5 — Error handling is not optional.
  Every method that can fail returns Result<T> or throws a typed exception.
  Never swallow exceptions with an empty catch block.
```

---

## 2. Naming Conventions

### .NET / C#

| Element | Convention | Example |
|---------|------------|---------|
| Namespace | `Company.Product.Layer` | `ToolEngine.Infrastructure.Persistence` |
| Class / Record | PascalCase, noun | `TenantCostRecord`, `ApprovalBehavior` |
| Interface | `I` + PascalCase noun | `IToolRegistry`, `ICacheProvider` |
| Method | PascalCase, verb-noun | `GetByIdAsync`, `MarkSucceeded` |
| Property | PascalCase, noun | `AllowedNamespaces`, `ExpiresAt` |
| Private field | `_camelCase` | `_db`, `_mediator`, `_logger` |
| Constant | PascalCase | `MaxRetryAttempts`, `DefaultTokenBudget` |
| Enum type | Singular PascalCase | `ApprovalRisk`, `CallerType` |
| Enum value | PascalCase | `ApprovalRisk.High`, `CallerType.AiAgent` |
| Async method | Suffix `Async` | `SaveChangesAsync`, `ReExecuteAsync` |
| Test method | `Given_When_Then` | `GivenExpiredApproval_WhenDecideIsCalled_ThenReturnsNotFound` |
| Generic param | `T`, `TInput`, `TOutput`, `TEntity` | `Result<TOutput>`, `IRepository<TEntity, TId>` |

### TypeScript

| Element | Convention | Example |
|---------|------------|---------|
| File | `kebab-case.ts` | `tool-invoker.tsx`, `use-invocation-status.ts` |
| Component | PascalCase | `ToolInvoker`, `RiskBadge` |
| Hook | `use` + PascalCase | `useInvocationStatus`, `useToolSearch` |
| Interface | PascalCase, no `I` prefix | `InvocationStatus`, `CatalogItem` |
| Enum | PascalCase | `ApprovalRisk` |
| Constant | `SCREAMING_SNAKE_CASE` | `MAX_RETRY_ATTEMPTS`, `DEFAULT_PAGE_SIZE` |
| Variable / param | `camelCase` | `invocationId`, `pageSize` |

---

## 3. Complexity Limits

### Cyclomatic Complexity

Maximum McCabe cyclomatic complexity per method: **10**

Complexity > 10 = mandatory refactor before merge. No exceptions.

```csharp
// VIOLATION — complexity 14: deeply nested conditions
public async Task ProcessAsync(Command cmd)
{
    if (cmd != null)
    {
        if (cmd.TenantId != null)
        {
            var tenant = await GetTenant(cmd.TenantId);
            if (tenant != null)
            {
                if (tenant.IsActive)
                {
                    if (tenant.IsNamespaceAllowed(cmd.Namespace))
                    {
                        // ... more nesting
                    }
                }
            }
        }
    }
}

// COMPLIANT — guard clauses flatten the structure
public async Task ProcessAsync(Command cmd)
{
    ArgumentNullException.ThrowIfNull(cmd);

    var tenant = await GetTenant(cmd.TenantId)
        ?? throw new TenantNotFoundException(cmd.TenantId);

    if (!tenant.IsActive)
        return Result.Failure(Error.Unauthorized("Tenant is inactive."));

    if (!tenant.IsNamespaceAllowed(cmd.Namespace))
        return Result.Failure(Error.Unauthorized("Namespace not allowed."));

    // happy path continues here
}
```

### Method length

- Hard limit: **50 lines** per method (excluding blank lines and comments).
- Soft target: **20 lines**. Methods over 20 lines should be reviewed for extraction.

### File length

- Hard limit: **400 lines** per file.
- Exception: generated files, EF Core migrations.

---

## 4. Null Safety Rules

```csharp
// RULE 1: Nullable reference types MUST be enabled on all projects
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
// Zero nullable warnings permitted at build time.

// RULE 2: Never use null for absence of a business value. Use Option/Result.
// Bad:
public Tenant? GetTenant(string id) => /* returns null if not found */

// Good: explicit absence
public Result<Tenant> GetTenant(string id)
    => _tenant is null ? Result.Failure<Tenant>(Error.NotFound("Tenant", id))
                       : Result.Success(_tenant);

// RULE 3: Null-forgiving operator (!) is BANNED except in:
//   - EF Core navigation property initialisation
//   - Test arrange blocks where null-safety is irrelevant
// Every ! in production code requires a comment explaining why it cannot be null.

// RULE 4: ArgumentNullException at public API boundaries only
public void Register(ITool tool)
{
    ArgumentNullException.ThrowIfNull(tool);   // OK: public method, external caller
    // Don't guard internal methods — trust the callers within the same assembly
}
```

---

## 5. Error Handling Rules

```csharp
// RULE 1: Never swallow exceptions
try { await DoSomethingAsync(); }
catch { }  // BANNED — loses the exception forever

// RULE 2: Only catch what you can handle
try { await DoSomethingAsync(); }
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
{
    await Task.Delay(TimeSpan.FromSeconds(1));
    // specific, actionable handling
}
// Let all other exceptions propagate

// RULE 3: Use Result<T> for expected failures (not exceptions)
// Exceptions: for programmer errors, violated contracts, infrastructure failures
// Result<T>: for business rule violations, validation failures, not-found

// RULE 4: Log at the boundary, not inside
// Bad: every method logs its own exceptions (noisy, duplicated)
// Good: the outermost handler (MediatR behavior, middleware) logs unhandled exceptions

// RULE 5: Include context in error messages
// Bad:  throw new Exception("Not found");
// Good: throw new TenantNotFoundException(tenantId);
//       Result.Failure(Error.NotFound("Tenant", tenantId));
```

---

## 6. Async / Await Rules

```csharp
// RULE 1: Async all the way — never .Result or .Wait()
// Bad (deadlock risk):
var tenant = _repo.GetByIdAsync(id).Result;

// Good:
var tenant = await _repo.GetByIdAsync(id);

// RULE 2: Always pass CancellationToken through the call chain
// Bad: public async Task ProcessAsync()
// Good: public async Task ProcessAsync(CancellationToken ct = default)

// RULE 3: ConfigureAwait(false) in library code, not in ASP.NET Core handlers
// ASP.NET Core doesn't use SynchronizationContext — ConfigureAwait(false) is
// neither needed nor harmful, but is noise. Omit it in ASP.NET Core projects.

// RULE 4: Avoid async void — always return Task
// Exception: event handlers in WinForms/WPF (not applicable here)
// Bad:  public async void HandleEvent()
// Good: public async Task HandleEventAsync()

// RULE 5: ValueTask only when microbenchmark evidence shows ITask overhead matters
// For all application-level code: use Task, not ValueTask
```

---

## 7. Code Review Checklist

This checklist is mandatory for every PR. The reviewer signs off on each item.

### Correctness
- [ ] Does the code do what the ticket requires?
- [ ] Are edge cases (empty collections, null, min/max values) handled?
- [ ] Is concurrent access safe (no shared mutable state without synchronization)?
- [ ] Are database queries inside loops avoided (N+1 query check)?

### Security
- [ ] No secrets, API keys, or passwords in code or config files
- [ ] No SQL string concatenation (parameterised queries only)
- [ ] No user input directly in file paths, shell commands, or LDAP queries
- [ ] OWASP Top 10 2025: does this change introduce injection, broken access control, or SSRF?
- [ ] Approval risk level is appropriate for the tool's potential impact

### Design
- [ ] Does this change belong in the correct layer (domain / application / infrastructure)?
- [ ] Is a new abstraction justified (has 2+ concrete implementations now)?
- [ ] Are existing abstractions respected (no direct infrastructure access from handlers)?
- [ ] Cyclomatic complexity ≤ 10 per method
- [ ] No duplicate code (3-rule checked)

### Observability
- [ ] New failure paths are logged at the appropriate level
- [ ] New tool metrics / telemetry recorded (if applicable)
- [ ] New domain event raised (if aggregate state changed)

### Tests
- [ ] Unit tests cover the happy path AND at least 2 failure scenarios
- [ ] New public API has integration test coverage
- [ ] Test names follow `Given_When_Then` convention
- [ ] No test logic that tests the framework (ASP.NET, EF Core) rather than the domain

### Documentation
- [ ] Public interfaces and methods with non-obvious behaviour have a comment explaining WHY
- [ ] SKILL file updated if a new pattern was introduced
- [ ] Breaking changes documented in CHANGELOG.md

---

## 8. Performance Anti-Patterns (Banned)

```csharp
// BANNED: N+1 query pattern
foreach (var tenantId in tenantIds)
{
    var tenant = await _db.Tenants.FindAsync(tenantId); // N+1
}
// Use: await _db.Tenants.Where(t => tenantIds.Contains(t.Id)).ToListAsync()

// BANNED: Synchronous I/O in async context
var bytes = File.ReadAllBytes(path);    // blocks thread pool thread
// Use: await File.ReadAllBytesAsync(path)

// BANNED: String concatenation in loops
var result = "";
foreach (var item in items) result += item;  // O(n²) allocations
// Use: string.Join, StringBuilder, or string.Create

// BANNED: LINQ inside tight loops on large collections
foreach (var item in million_items)
{
    if (otherList.Contains(item)) { ... }  // O(n) per iteration = O(n²)
}
// Use: var set = otherList.ToHashSet(); then set.Contains(item)

// BANNED: Blocking on async from a constructor
public class MyService
{
    public MyService()
    {
        _data = LoadDataAsync().Result;  // deadlock risk, blocks thread
    }
}
// Use: factory method or lazy initialisation pattern
```

---

## 9. Forbidden Patterns

The following are unconditionally prohibited and will cause PR rejection:

| Pattern | Reason |
|---------|--------|
| `Thread.Sleep` in production code | Blocks thread pool; use `Task.Delay` |
| `static` mutable shared state | Thread safety, testability, multi-tenancy |
| `GC.Collect()` | Interference with runtime memory management |
| `catch (Exception ex) { }` (empty catch) | Silent failure |
| Hardcoded connection strings | Secrets must come from ISecretVault or env vars |
| `Encoding.Default` | Culture-sensitive, platform-dependent; use `Encoding.UTF8` |
| Direct `DbContext` in controller/endpoint | Must go through repository or handler |
| `IServiceLocator` / `ServiceLocator.Current` | Service-locator anti-pattern |
| `AutoMapper` with `ReverseMap` | Hides mapping bugs; prefer explicit projection |
| `[Obsolete]` without replacement comment | Leaves developers with no migration path |

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
