---
name: toolengine-sonar-quality-gate
description: >
  Defines SonarCloud and SonarQube configuration, quality gate definitions,
  CI/CD integration, suppression policy, and security hotspot review process
  for all ONE BCG application development. Covers: project setup, quality gate
  rules (0 new bugs, 0 new vulnerabilities, 80% coverage, 3% duplication max,
  A-rating on new code smells), CI integration with GitHub Actions, suppression
  with mandatory justification, security hotspot triage workflow, and branch
  analysis configuration. Apply this SKILL to every new service at project
  inception.
classification: Confidential - Internal Use Only
---

# Sonar Quality Gate — ONE BCG Development Platform

## Purpose

SonarCloud is the primary automated code quality and security scanner for all
ONE BCG projects. Every PR must pass the Sonar quality gate before merge.
"New code" is measured against the previous analysis — you own the quality of
your changes.

---

## 1. SonarCloud Project Setup

### Prerequisites

- SonarCloud organization: `onebcg` (https://sonarcloud.io/organizations/onebcg)
- GitHub repository connected to SonarCloud
- `SONAR_TOKEN` secret added to GitHub repository secrets

### `sonar-project.properties` — root of every project

```properties
# Project identity
sonar.projectKey=onebcg_toolengine
sonar.projectName=ONE BCG ToolEngine
sonar.organization=onebcg

# Source settings
sonar.sources=src
sonar.tests=tests
sonar.language=cs

# Coverage
sonar.cs.opencover.reportsPaths=**/coverage/coverage.opencover.xml
sonar.cs.vstest.reportsPaths=**/TestResults/*.trx

# Exclusions — generated code, migrations, scaffolding
sonar.exclusions=\
  **/Migrations/**,\
  **/Generated/**,\
  **/obj/**,\
  **/bin/**,\
  **/wwwroot/lib/**,\
  frontend/node_modules/**

sonar.coverage.exclusions=\
  **/Migrations/**,\
  **/Program.cs,\
  **/Generated/**

# Duplication exclusions (generated/auto-created code)
sonar.cpd.exclusions=**/Migrations/**,**/Generated/**
```

---

## 2. Quality Gate Definition

### "ONE BCG Standard" gate — applied to all projects

This gate must be created in the SonarCloud UI under `Quality Gates`.
Name it `ONE BCG Standard` and set it as the default for the organization.

#### New Code conditions (applies to PRs and new branch analysis)

| Metric | Operator | Threshold | Why |
|--------|----------|-----------|-----|
| Bugs (new) | is greater than | 0 | Zero tolerance for new bugs |
| Vulnerabilities (new) | is greater than | 0 | Zero tolerance for security issues |
| Security Hotspots Reviewed | is less than | 100% | All hotspots must be triaged |
| Code Smells (new, severity A) | is greater than | 0 | No new maintainability debt |
| Coverage (new code) | is less than | 80% | Minimum test coverage for new code |
| Duplicated Lines % (new code) | is greater than | 3% | DRY enforcement |
| Reliability Rating (new code) | is worse than | A | No new reliability issues |
| Security Rating (new code) | is worse than | A | No new security issues |
| Maintainability Rating (new code) | is worse than | A | No new code smells |

#### Overall code conditions (applies to main branch analysis)

| Metric | Operator | Threshold |
|--------|----------|-----------|
| Coverage | is less than | 75% |
| Duplicated Lines % | is greater than | 5% |
| Maintainability Rating | is worse than | B |

---

## 3. GitHub Actions CI Integration

### `.github/workflows/sonar.yml`

```yaml
name: SonarCloud Analysis
on:
  push:
    branches: [main, develop]
  pull_request:
    types: [opened, synchronize, reopened]

jobs:
  sonar:
    name: SonarCloud
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 0    # Full history required for blame and new-code detection

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Install SonarScanner
        run: |
          dotnet tool install --global dotnet-sonarscanner
          dotnet tool install --global dotnet-coverage

      - name: Cache SonarCloud packages
        uses: actions/cache@v4
        with:
          path: ~/.sonar/cache
          key: ${{ runner.os }}-sonar
          restore-keys: ${{ runner.os }}-sonar

      - name: Begin Sonar Analysis
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          SONAR_TOKEN:  ${{ secrets.SONAR_TOKEN }}
        run: |
          dotnet sonarscanner begin \
            /k:"onebcg_toolengine" \
            /o:"onebcg" \
            /d:sonar.token="${{ secrets.SONAR_TOKEN }}" \
            /d:sonar.host.url="https://sonarcloud.io" \
            /d:sonar.cs.opencover.reportsPaths="**/coverage/coverage.opencover.xml" \
            /d:sonar.cs.vstest.reportsPaths="**/TestResults/*.trx"

      - name: Build
        run: dotnet build src/ToolEngine.sln --configuration Release

      - name: Run Tests with Coverage
        run: |
          dotnet-coverage collect \
            'dotnet test src/ToolEngine.sln \
              --configuration Release \
              --logger trx \
              --results-directory TestResults' \
            -f xml \
            -o coverage/coverage.opencover.xml

      - name: End Sonar Analysis
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: |
          dotnet sonarscanner end \
            /d:sonar.token="${{ secrets.SONAR_TOKEN }}"
```

---

## 4. Sonar Rule Configuration

### `SonarQube.Analysis.xml` — `.sonarqube/` in project root

Customise active rules for the ONE BCG C# profile. The profile inherits from
`Sonar way` and adds or adjusts the following:

```xml
<SonarQubeAnalysisProperties xmlns:xsi="..." xmlns:xsd="...">
  <Property Name="sonar.cs.roslyn.reportFilePaths">**/*.diagnostics.json</Property>
</SonarQubeAnalysisProperties>
```

### Rules adjustment table

| Rule ID | Rule Name | Action | Reason |
|---------|-----------|--------|--------|
| `S1135` | Track uses of "TODO" tags | MAJOR → CRITICAL | TODOs in merged code are tech debt |
| `S3776` | Cognitive Complexity | threshold: 10 (not default 15) | Aligned with quality-standards.md |
| `S4457` | Params validation in public methods | MINOR → MAJOR | Enforce null guards |
| `S2699` | Tests should include assertions | CRITICAL (kept) | Never waive this |
| `S1481` | Unused local variables | ERROR | Zero unused vars in production code |
| `S125`  | Sections of code should not be commented out | MAJOR | Commented code must be deleted |

---

## 5. Issue Suppression Policy

### When suppression is acceptable

Suppression is a last resort. The bar is high:

| Scenario | Acceptable | Required justification |
|----------|------------|----------------------|
| False positive on generated code | Yes | `// Sonar: false positive — generated by EF Core migration tooling` |
| Platform-specific pattern understood to be safe | Yes | Link to relevant doc or RFC |
| Third-party library constraint | Yes | Name the library and the constraint |
| "Too noisy" | **No** | Fix the code instead |
| "We'll fix it later" | **No** | Tech debt should be a tracked issue |

### How to suppress

```csharp
// CORRECT — inline suppression with justification
#pragma warning disable S3776 // Cognitive complexity > 10
// Sonar: this method maps 12 tool type variants; extracting sub-methods
// would obscure the mapping intent. Reviewed by tech lead 2026-05-19.
public ITool ResolveFromType(string typeCode) { ... }
#pragma warning restore S3776

// BANNED — no justification
#pragma warning disable S3776
public ITool ResolveFromType(string typeCode) { ... }
#pragma warning restore S3776

// CORRECT — attribute suppression for test method
[ExcludeFromCodeCoverage]   // OK on test helper, not on production code
public static Tenant BuildTestTenant() { ... }
```

### `// NOSONAR` tag

Use `// NOSONAR` only on single lines and only with a justification comment
on the preceding line:

```csharp
// Sonar: password variable name triggers S2068 but this is a variable
// receiving a hashed value from the vault, not a hardcoded password.
var hashedPassword = await _vault.GetSecretAsync("auth", "db", "password"); // NOSONAR
```

---

## 6. Security Hotspot Review Process

Security hotspots are flagged by Sonar as code that may be sensitive but
requires a human decision. All hotspots must be triaged before the quality
gate passes.

### Hotspot triage workflow

```
Sonar flags hotspot
      │
      ├── Is it a true security risk?
      │         │
      │       YES: Create security ticket (priority: HIGH or CRITICAL)
      │             Fix in the same PR or block merge until fixed
      │         │
      │       NO: Mark as "Safe" in SonarCloud UI with justification note
      │             Note must explain WHY it is safe (1–2 sentences)
      │
      └── Mark "Reviewed" in SonarCloud — gate requires 100% reviewed
```

### Common hotspots in ToolEngine and their correct classification

| Hotspot | Classification | Justification |
|---------|---------------|---------------|
| `RandomNumberGenerator` usage | Safe | CSPRNG — intentionally cryptographically random |
| `Convert.ToHexString` | Safe | Output encoding, not input parsing |
| JWT validation parameters | Safe | Reviewed per Phase 1 SKILL — all required validations enabled |
| SQL interpolation in EF | Review carefully | Only safe when using `FormattableString` / `$""` (EF parameterises) |
| `HttpClient.DangerousAcceptAny...` | RISK — fix required | Acceptable only in test fixtures, never in production |
| `Process.Start` | RISK — fix required | Only permitted in CLI tool, never in API/worker |

---

## 7. Branch Analysis Configuration

### Main branch — full analysis

The `main` branch receives full historical analysis and tracks overall code
quality. All quality gate conditions apply.

### PR branches — new code only

PRs are analysed against the new-code period only. The developer is responsible
for the quality of their own changes — not the pre-existing technical debt.

### Feature branch naming conventions for Sonar

```yaml
# In sonar.yml — only run full analysis on meaningful branches
on:
  push:
    branches:
      - main
      - 'release/**'
      - 'hotfix/**'
  pull_request:
    branches: [main]
# Do NOT run Sonar on every feature branch push — too noisy and expensive
```

---

## 8. Local Analysis (Pre-Commit)

Run Sonar locally before pushing to catch issues early:

```bash
# Requires SonarScanner CLI installed
# https://docs.sonarcloud.io/advanced-setup/ci-based-analysis/sonarscanner-cli/

# Run a local analysis against your local SonarQube (if available)
dotnet sonarscanner begin \
  /k:"onebcg_toolengine_local" \
  /d:sonar.host.url="http://localhost:9000" \
  /d:sonar.login="$SONAR_LOCAL_TOKEN"

dotnet build
dotnet test --collect:"XPlat Code Coverage"

dotnet sonarscanner end /d:sonar.login="$SONAR_LOCAL_TOKEN"
```

---

## 9. Sonar Quality Dashboard

### Metrics to track on the main branch dashboard

| Widget | Metric | Target |
|--------|--------|--------|
| Reliability | Bugs | 0 |
| Security | Vulnerabilities | 0 |
| Security Review | Hotspots reviewed | 100% |
| Maintainability | Technical debt ratio | < 5% |
| Coverage | Line coverage | ≥ 80% |
| Duplications | Duplicated lines | < 3% |
| Lines of code | Size | — (tracking only) |

---

## Quality Gate Completion Checklist

### Project setup
- [ ] `sonar-project.properties` at repository root with correct project key and organisation
- [ ] `SONAR_TOKEN` secret added to GitHub repository
- [ ] SonarCloud project created and linked to GitHub repository
- [ ] `ONE BCG Standard` quality gate applied to project

### CI integration
- [ ] `sonar.yml` GitHub Actions workflow with `fetch-depth: 0`
- [ ] Coverage collected in OpenCover XML format (not cobertura) for Sonar
- [ ] `.trx` test results collected and passed to Sonar
- [ ] Analysis runs on PRs and on main branch push
- [ ] Quality gate failure blocks PR merge (configured in GitHub branch protection)

### Quality gate rules
- [ ] New bugs: 0
- [ ] New vulnerabilities: 0
- [ ] Security hotspots reviewed: 100%
- [ ] New code coverage: ≥ 80%
- [ ] New code duplication: ≤ 3%
- [ ] New code maintainability: A rating

### Suppressions
- [ ] Every `#pragma warning disable S*` has a justification comment
- [ ] Every `// NOSONAR` has a preceding justification comment
- [ ] No suppressions for `S2699` (tests must have assertions — never suppress)
- [ ] All security hotspots triaged as Safe or fixed

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
