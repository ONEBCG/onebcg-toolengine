# AWS Lambda + RDS Deployment Runbook

**ONE BCG ToolEngine v2026 — B2B Payment Processing POC**

> **Purpose:** Step-by-step runbook to deploy ToolEngine on AWS Lambda (API + UI) with RDS PostgreSQL.
> Every section is self-contained and executable in sequence. Checkboxes track progress.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Infrastructure Setup](#2-infrastructure-setup)
   - 2.1 IAM & CLI
   - 2.2 Security Groups
   - 2.3 RDS PostgreSQL
   - 2.4 Secrets Manager
   - 2.5 Lambda IAM Role
3. [Code Changes](#3-code-changes)
   - 3.1 NuGet Packages
   - 3.2 API — Program.cs
   - 3.3 API — appsettings.Production.json
   - 3.4 API — aws-lambda-tools-defaults.json
   - 3.5 UI — Program.cs
   - 3.6 UI — appsettings.Production.json
   - 3.7 UI — aws-lambda-tools-defaults.json
   - 3.8 EF Core Postgres Migrations
4. [Build & Deploy](#4-build--deploy)
   - 4.1 Deploy API Lambda
   - 4.2 Deploy UI Lambda
5. [Post-Deploy Configuration](#5-post-deploy-configuration)
6. [Verification Checklist](#6-verification-checklist)
7. [Ongoing Operations](#7-ongoing-operations)
8. [Cost Reference](#8-cost-reference)
9. [Rollback](#9-rollback)

---

## Deployed Environment (ap-south-1)

> This section reflects the actual live deployment. URLs and names below are real.

| Resource | Value |
|----------|-------|
| AWS Region | `ap-south-1` (Mumbai) |
| AWS Account | `515674010661` |
| API Lambda | `ToolEngine-Api` |
| UI Lambda | `ToolEngine-Ui` |
| API Lambda URL | `https://vsyn67ytbwgdb3ysakfgraflvy0mwrts.lambda-url.ap-south-1.on.aws/` |
| UI Lambda URL | `https://jxcd4ft6mcth32v5gbxc6efevm0lkolz.lambda-url.ap-south-1.on.aws/` |
| API Gateway HTTP API | `h708rohph9.execute-api.ap-south-1.amazonaws.com` |
| S3 Deploy Bucket | `toolengine-lambda-deploys-515674010661` |
| Lambda IAM Role | `arn:aws:iam::515674010661:role/toolengine-lambda-role` |

**API Gateway routing:**
- `$default` → UI Lambda (serves index.html and /config)
- `/api/*` → API Lambda (all REST endpoints)

---

## Architecture Overview

```
Browser
  │
  ├─── API Gateway HTTP API  (h708rohph9.execute-api.ap-south-1.amazonaws.com)
  │    ├── $default  → UI Lambda Function URL  (BUFFERED)
  │    │    • GET /       → index.html
  │    │    • GET /config → { "apiBaseUrl": "https://[api-url]" }
  │    │
  │    └── /api/*  → API Lambda Function URL  (RESPONSE_STREAM — enables SSE chat)
  │         • /api/v1/*              — all payment endpoints
  │         • /api/v1/chat/stream    — SSE streaming (LLM chat)
  │         • /swagger, /health, /dev/token
  │
  ├─── UI Lambda Function URL (direct)
  │    https://jxcd4ft6mcth32v5gbxc6efevm0lkolz.lambda-url.ap-south-1.on.aws/
  │
  └─── API Lambda Function URL (direct)
       https://vsyn67ytbwgdb3ysakfgraflvy0mwrts.lambda-url.ap-south-1.on.aws/
            │
            ├── RDS PostgreSQL db.t3.micro  (publicly accessible, SG-restricted)
            ├── AWS Secrets Manager         (JWT, Anthropic key, Google key, DB connection)
            └── CloudWatch Logs             (automatic)
```

---

## 1. Prerequisites

- [ ] AWS account created
- [ ] AWS CLI v2 installed (`winget install -e --id Amazon.AWSCLI`)
- [ ] .NET 8 SDK installed locally
- [ ] .NET Lambda tools installed (`dotnet tool install -g Amazon.Lambda.Tools`)
- [ ] Project builds locally: `dotnet build ToolEngine.sln`
- [ ] Anthropic API key available (`sk-ant-api03-...`) and/or Google API key (`AIzaSy...`)
- [ ] Note your AWS Account ID (12-digit number, visible in Console top-right)
- [ ] Choose region — this runbook uses **`ap-south-1`** (Mumbai). Replace if using another.

---

## 2. Infrastructure Setup

### 2.1 IAM & CLI

**Create IAM user `toolengine-deploy`**

In AWS Console → IAM → Users → Create user:

- Username: `toolengine-deploy`
- Attach policies directly:
  - `AWSLambda_FullAccess`
  - `AmazonRDSFullAccess`
  - `SecretsManagerReadWrite`
  - `IAMFullAccess`
  - `CloudWatchLogsFullAccess`

Create access key → Access key use case: CLI → Download CSV.

**Configure AWS CLI**

```bash
aws configure
# AWS Access Key ID:     [from CSV]
# AWS Secret Access Key: [from CSV]
# Default region name:   ap-south-1
# Default output format: json
```

**Verify**

```bash
aws sts get-caller-identity
# Expected: { "Account": "515674010661", "Arn": "arn:aws:iam::..." }
```

- [ ] IAM user created
- [ ] AWS CLI configured
- [ ] `aws sts get-caller-identity` succeeds

---

### 2.2 Security Groups

> Uses the **default VPC**. Do not create a new VPC for POC.

**Security group for Lambda reference (toolengine-lambda-sg)**

```bash
aws ec2 create-security-group \
  --group-name toolengine-lambda-sg \
  --description "ToolEngine Lambda reference SG" \
  --region ap-south-1

# Save the returned GroupId — e.g. sg-0abc123
# LAMBDA_SG_ID=sg-0abc123
```

**Security group for RDS (toolengine-rds-sg)**

```bash
aws ec2 create-security-group \
  --group-name toolengine-rds-sg \
  --description "ToolEngine RDS - allow Postgres" \
  --region ap-south-1

# Save the returned GroupId — e.g. sg-0def456
# RDS_SG_ID=sg-0def456

# Allow Postgres from everywhere (POC only — restrict in production)
aws ec2 authorize-security-group-ingress \
  --group-id $RDS_SG_ID \
  --protocol tcp \
  --port 5432 \
  --cidr 0.0.0.0/0 \
  --region ap-south-1
```

> **Note:** Allowing `0.0.0.0/0` on port 5432 is POC-only. The RDS master password is still required for all connections. Restrict to specific CIDRs before production use.

- [ ] `toolengine-lambda-sg` created → ID noted: `sg-____________`
- [ ] `toolengine-rds-sg` created → ID noted: `sg-____________`
- [ ] Port 5432 inbound rule added to RDS SG

---

### 2.3 RDS PostgreSQL

**Create the RDS instance**

In AWS Console → RDS → Create database:

| Setting | Value |
|---------|-------|
| Creation method | Standard |
| Engine | PostgreSQL |
| Engine version | PostgreSQL 16.x (latest) |
| Templates | **Free tier** |
| DB instance identifier | `toolengine-poc` |
| Master username | `toolengine` |
| Master password | Auto-generate or set manually → **record it** |
| DB instance class | `db.t3.micro` |
| Storage type | gp2 |
| Allocated storage | 20 GB |
| Multi-AZ | No |
| **Connectivity → Public access** | **Yes** |
| VPC security groups | `toolengine-rds-sg` |
| Database port | 5432 |
| Additional config → Initial database name | `toolenginedb` |
| Automated backups | Enable, 1 day retention |
| Encryption | Disable (POC) |

Click **Create** → wait ~5 minutes.

**Record outputs**

```
RDS Endpoint:  toolengine-poc.XXXXXXXX.ap-south-1.rds.amazonaws.com
Database:      toolenginedb
Username:      toolengine
Password:      [recorded above]
Port:          5432
```

**Test connectivity from local machine** (optional but recommended)

```bash
psql "host=toolengine-poc.XXXX.ap-south-1.rds.amazonaws.com \
      port=5432 dbname=toolenginedb user=toolengine \
      sslmode=require"
# Enter password → should connect
# \q to exit
```

- [ ] RDS instance created and available
- [ ] Endpoint recorded
- [ ] Connectivity verified

---

### 2.4 Secrets Manager

Create 4 secrets. Run these AWS CLI commands (PowerShell — single line each), replacing the placeholder values:

```powershell
# 1. JWT Secret (min 32 chars)
aws secretsmanager create-secret --name "toolengine/jwt-secret" --secret-string "prod-jwt-secret-min-32-bytes-replace-in-production!!" --region ap-south-1

# 2. Anthropic API Key
aws secretsmanager create-secret --name "toolengine/anthropic-api-key" --secret-string "sk-ant-api03-REPLACE-WITH-REAL-KEY" --region ap-south-1

# 3. Google API Key (for Gemini — set to empty string if using Claude only)
aws secretsmanager create-secret --name "toolengine/google-api-key" --secret-string "AIzaSy-REPLACE-WITH-REAL-KEY" --region ap-south-1

# 4. Database Connection String
# Replace ENDPOINT with RDS endpoint from §2.3 and PASSWORD with RDS master password
aws secretsmanager create-secret --name "toolengine/db-connection" --secret-string "Host=ENDPOINT;Port=5432;Database=toolenginedb;Username=toolengine;Password=PASSWORD;SslMode=Require;Trust Server Certificate=true;" --region ap-south-1
```

> **PowerShell note:** Use single-line commands. Bash-style `\` line continuation does not work in PowerShell — use `` ` `` (backtick) or keep each command on one line.

**Verify**

```powershell
aws secretsmanager list-secrets --region ap-south-1 --query "SecretList[?starts_with(Name,'toolengine')].Name"
# Expected:
# ["toolengine/anthropic-api-key", "toolengine/db-connection", "toolengine/google-api-key", "toolengine/jwt-secret"]
```

**Actual deployed secret ARNs (ap-south-1 · account 515674010661):**

| Secret name | ARN suffix |
|-------------|-----------|
| `toolengine/db-connection` | `...secret:toolengine/db-connection-*` |
| `toolengine/jwt-secret` | `...secret:toolengine/jwt-secret-*` |
| `toolengine/anthropic-api-key` | `...secret:toolengine/anthropic-api-key-*` |
| `toolengine/google-api-key` | `...secret:toolengine/google-api-key-t0HKnQ` |

- [ ] `toolengine/jwt-secret` created
- [ ] `toolengine/anthropic-api-key` created (real key, or placeholder if using Gemini)
- [ ] `toolengine/google-api-key` created (real key, or empty string if using Claude)
- [ ] `toolengine/db-connection` created (real endpoint + password)

---

### 2.5 Lambda IAM Role

```powershell
# Create trust policy file
@'
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": { "Service": "lambda.amazonaws.com" },
    "Action": "sts:AssumeRole"
  }]
}
'@ | Out-File -FilePath "$env:TEMP\lambda-trust.json" -Encoding utf8

# Create the role
aws iam create-role --role-name toolengine-lambda-role --assume-role-policy-document "file://$env:TEMP\lambda-trust.json"

# Attach required policies
aws iam attach-role-policy --role-name toolengine-lambda-role --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole

aws iam attach-role-policy --role-name toolengine-lambda-role --policy-arn arn:aws:iam::aws:policy/SecretsManagerReadWrite

# Record the Role ARN
aws iam get-role --role-name toolengine-lambda-role --query "Role.Arn" --output text
# Output: arn:aws:iam::515674010661:role/toolengine-lambda-role
```

The `SecretsManagerReadWrite` policy grants access to all 4 secrets (`db-connection`, `jwt-secret`, `anthropic-api-key`, `google-api-key`). For tighter production security, replace with a resource-scoped inline policy listing each secret ARN explicitly.

- [ ] `toolengine-lambda-role` created
- [ ] Role ARN recorded: `arn:aws:iam::____________:role/toolengine-lambda-role`

---

## 3. Code Changes

> All paths are relative to the solution root:
> `D:\WorkingFolder\ONEBCG v2026\paymentprocessingpoc\code\`
>
> **No business logic changes.** All changes are in the hosting/infrastructure layer only.

---

### 3.1 NuGet Packages

**File:** `src/Hosts/ToolEngine.Api/ToolEngine.Api.csproj`

Add inside `<ItemGroup>`:

```xml
<PackageReference Include="Amazon.Lambda.AspNetCoreServer.Hosting" Version="8.*" />
<PackageReference Include="AWSSDK.SecretsManager" Version="3.*" />
```

**File:** `src/Hosts/ToolEngine.Ui/ToolEngine.Ui.csproj`

Add inside `<ItemGroup>`:

```xml
<PackageReference Include="Amazon.Lambda.AspNetCoreServer.Hosting" Version="8.*" />
<PackageReference Include="AWSSDK.SecretsManager" Version="3.*" />
```

**Verify packages restore:**

```powershell
cd "D:\WorkingFolder\ONEBCG v2026\paymentprocessingpoc\code"
dotnet restore
```

- [ ] API csproj updated
- [ ] UI csproj updated
- [ ] `dotnet restore` succeeds

---

### 3.2 API — Program.cs

**File:** `src/Hosts/ToolEngine.Api/Program.cs`

#### Change A — Add using directives at the top of the file

After the existing `using` statements, add:

```csharp
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
```

#### Change B — Secrets Manager block

Insert this block **before** `var builder = WebApplication.CreateBuilder(args);`:

```csharp
// ── AWS Secrets Manager — inject production secrets before builder ─────────────
// Runs only when ASPNETCORE_ENVIRONMENT=Production (i.e. on Lambda).
// Double-underscore maps to IConfiguration hierarchy: Database__ConnectionString
// resolves to the "Database:ConnectionString" config key.
if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
{
    var awsRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "ap-south-1";
    var smClient  = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(awsRegion));

    static async Task<string> FetchSecret(AmazonSecretsManagerClient client, string secretName)
    {
        var response = await client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = secretName });
        return response.SecretString;
    }

    // Inject as environment variables — IConfiguration env provider reads these automatically.
    Environment.SetEnvironmentVariable("Database__ConnectionString",
        await FetchSecret(smClient, "toolengine/db-connection"));

    Environment.SetEnvironmentVariable("Jwt__SecretKey",
        await FetchSecret(smClient, "toolengine/jwt-secret"));

    // ClaudeProvider reads ANTHROPIC_API_KEY directly from the environment.
    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY",
        await FetchSecret(smClient, "toolengine/anthropic-api-key"));

    // GeminiProvider reads GOOGLE_API_KEY directly from the environment.
    Environment.SetEnvironmentVariable("GOOGLE_API_KEY",
        await FetchSecret(smClient, "toolengine/google-api-key"));
}
```

#### Change C — Lambda hosting block

Insert this block **after** `var builder = WebApplication.CreateBuilder(args);`:

```csharp
// ── Lambda hosting — transparent replacement for Kestrel on AWS Lambda ─────────
// When AWS_LAMBDA_FUNCTION_NAME is set, the app is running on Lambda.
// RESPONSE_STREAM invoke mode is set in aws-lambda-tools-defaults.json — no code flag needed.
// When running locally, this block is skipped and Kestrel starts normally.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
    builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
```

- [ ] Using directives added
- [ ] Secrets Manager block added before builder (includes GOOGLE_API_KEY)
- [ ] Lambda hosting block added after builder

---

### 3.3 API — appsettings.Production.json

**Create file:** `src/Hosts/ToolEngine.Api/appsettings.Production.json`

```json
{
  "Database": {
    "Provider": "postgres",
    "ConnectionString": ""
  },
  "Cache": {
    "Provider": "memory"
  },
  "LLM": {
    "Provider": "claude",
    "Streaming": true,
    "AutonomousToolSelection": true,
    "_comment_providers": "claude | openai | gemini",
    "_comment_apikeys": "Injected at runtime from Secrets Manager via env vars: ANTHROPIC_API_KEY / OPENAI_API_KEY / GOOGLE_API_KEY",
    "Claude": {
      "ApiKey": "",
      "Model": "claude-sonnet-4-6",
      "BaseUrl": "https://api.anthropic.com/v1/messages"
    },
    "OpenAI": {
      "ApiKey": "",
      "Model": "gpt-4o",
      "BaseUrl": "https://api.openai.com/v1/chat/completions"
    },
    "Gemini": {
      "ApiKey": "",
      "Model": "gemini-2.5-flash",
      "BaseUrl": "https://generativelanguage.googleapis.com/v1beta"
    }
  },
  "Jwt": {
    "Issuer": "toolengine-api",
    "Audience": "toolengine-clients",
    "SecretKey": ""
  },
  "RateLimit": {
    "Window": "00:01:00",
    "PermitLimit": 60,
    "QueueLimit": 0
  },
  "Cors": {
    "AllowedOrigins": "PLACEHOLDER_UPDATE_AFTER_DEPLOY"
  },
  "Auth": {
    "AllowedDomain": "onebcg.com",
    "Google": {
      "ClientId": "314187099083-u5j430ki6clqof53v9p218aq8rkocc9n.apps.googleusercontent.com"
    }
  },
  "Payment": {
    "ResumeVerificationSecret": "prod-resume-verify-key-replace-me"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

> `ConnectionString`, `SecretKey`, and `ApiKey` fields are blank — injected at runtime via environment variables from Secrets Manager (Section 3.2 Change B). `PLACEHOLDER_UPDATE_AFTER_DEPLOY` is updated in Section 5 after Lambda URLs are known.
>
> To switch LLM provider on Lambda without a redeploy, update the `LLM__Provider` environment variable on the function configuration:
> ```powershell
> aws lambda update-function-configuration --function-name ToolEngine-Api --environment "Variables={ASPNETCORE_ENVIRONMENT=Production,AWS_DEFAULT_REGION=ap-south-1,LLM__Provider=gemini}" --region ap-south-1
> ```

- [ ] File created

---

### 3.4 API — aws-lambda-tools-defaults.json

**Create file:** `src/Hosts/ToolEngine.Api/aws-lambda-tools-defaults.json`

Replace `YOUR_ACCOUNT_ID` with your 12-digit AWS account number.

```json
{
  "profile": "default",
  "region": "ap-south-1",
  "configuration": "Release",
  "framework": "net8.0",
  "function-runtime": "dotnet8",
  "function-memory-size": 512,
  "function-timeout": 900,
  "function-handler": "ToolEngine.Api",
  "function-name": "ToolEngine-Api",
  "function-role": "arn:aws:iam::YOUR_ACCOUNT_ID:role/toolengine-lambda-role",
  "function-url-enable": true,
  "function-url-invoke-mode": "RESPONSE_STREAM",
  "function-url-auth-type": "NONE",
  "s3-bucket": "toolengine-lambda-deploys-YOUR_ACCOUNT_ID",
  "environment-variables": "ASPNETCORE_ENVIRONMENT=Production;AWS_DEFAULT_REGION=ap-south-1",
  "msbuild-parameters": "--self-contained false"
}
```

> `function-url-invoke-mode: RESPONSE_STREAM` enables SSE streaming for the chat endpoint.
> `function-timeout: 900` = 15 minutes — maximum Lambda timeout, required for long SSE connections.
> Function name is `ToolEngine-Api` (capital T, capital E) — matches the actual deployed function.

- [ ] File created
- [ ] `YOUR_ACCOUNT_ID` replaced with real account ID (`515674010661`)

---

### 3.5 UI — Program.cs

**File:** `src/Hosts/ToolEngine.Ui/Program.cs`

#### Change A — Add using directives

After existing `using` statements, add:

```csharp
using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
```

#### Change B — Security headers

The UI `Program.cs` already contains the full COOP/COEP + M7 headers block. The production CSP uses the API Gateway origin for `connect-src` and allows `frame-src accounts.google.com` (required for Google Identity Services popup). Verify the production branch of the CSP in `Ui/Program.cs` reads:

```csharp
// Production CSP (app.Environment.IsDevelopment() == false)
csp =
    "default-src 'self'; " +
    "script-src 'self' 'unsafe-inline' accounts.google.com; " +
    "style-src 'self' 'unsafe-inline' fonts.googleapis.com accounts.google.com; " +
    "font-src fonts.gstatic.com; " +
    "img-src 'self' data: assets.onebcg.com; " +
    "frame-src accounts.google.com; " +
    "connect-src 'self' https://h708rohph9.execute-api.ap-south-1.amazonaws.com accounts.google.com";
```

> `frame-src accounts.google.com` and `style-src accounts.google.com` are required for the Google Sign-In popup. Omitting either causes Chrome to block the GSI credential flow.
> COOP/COEP remain `unsafe-none` — stricter values block the Google `window.postMessage` channel.

#### Change C — Lambda hosting block

Insert **after** `var builder = WebApplication.CreateBuilder(args);`:

```csharp
// ── Lambda hosting for UI ─────────────────────────────────────────────────────
// UI uses BUFFERED mode (no streaming needed — serves static files + /config).
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
    builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
```

- [ ] Using directives added
- [ ] Security headers added (M7)
- [ ] Lambda hosting block added

---

### 3.6 UI — appsettings.Production.json

**Create file:** `src/Hosts/ToolEngine.Ui/appsettings.Production.json`

```json
{
  "ApiBaseUrl": "PLACEHOLDER_UPDATE_AFTER_DEPLOY",
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

> `ApiBaseUrl` will be set to the API Lambda Function URL in Section 5 after deploy.

- [ ] File created

---

### 3.7 UI — aws-lambda-tools-defaults.json

**Create file:** `src/Hosts/ToolEngine.Ui/aws-lambda-tools-defaults.json`

Replace `YOUR_ACCOUNT_ID` with your 12-digit AWS account number.

```json
{
  "profile": "default",
  "region": "ap-south-1",
  "configuration": "Release",
  "framework": "net8.0",
  "function-runtime": "dotnet8",
  "function-memory-size": 256,
  "function-timeout": 30,
  "function-handler": "ToolEngine.Ui",
  "function-name": "ToolEngine-Ui",
  "function-role": "arn:aws:iam::YOUR_ACCOUNT_ID:role/toolengine-lambda-role",
  "function-url-enable": true,
  "function-url-invoke-mode": "BUFFERED",
  "function-url-auth-type": "NONE",
  "s3-bucket": "toolengine-lambda-deploys-YOUR_ACCOUNT_ID",
  "environment-variables": "ASPNETCORE_ENVIRONMENT=Production;AWS_DEFAULT_REGION=ap-south-1",
  "msbuild-parameters": "--self-contained false"
}
```

> Function name is `ToolEngine-Ui` (capital T, capital E) — matches the actual deployed function.

- [ ] File created
- [ ] `YOUR_ACCOUNT_ID` replaced with real account ID (`515674010661`)

---

### 3.8 EF Core Postgres Migrations

The existing migrations use SQLite column types (`type: "TEXT"` for GUIDs, etc.). These are incompatible with PostgreSQL. They must be deleted and regenerated using the Postgres provider.

**Step 1 — Point local environment to Postgres temporarily**

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Database__Provider = "postgres"
$env:Database__ConnectionString = "Host=toolengine-poc.XXXX.ap-south-1.rds.amazonaws.com;Port=5432;Database=toolenginedb;Username=toolengine;Password=YOUR_PASSWORD;SslMode=Require;Trust Server Certificate=true;"
```

**Step 2 — Delete existing migrations**

```powershell
cd "D:\WorkingFolder\ONEBCG v2026\paymentprocessingpoc\code"
Remove-Item src\Infrastructure\ToolEngine.Infrastructure\Persistence\Migrations\* -Recurse -Force
```

**Step 3 — Regenerate migrations for Postgres**

```powershell
dotnet ef migrations add InitialCreate `
  --project src\Infrastructure\ToolEngine.Infrastructure `
  --startup-project src\Hosts\ToolEngine.Api `
  --output-dir Persistence\Migrations

dotnet ef migrations add AddScenarioExecution `
  --project src\Infrastructure\ToolEngine.Infrastructure `
  --startup-project src\Hosts\ToolEngine.Api `
  --output-dir Persistence\Migrations
```

**Step 4 — Restore local environment**

```powershell
Remove-Item env:ASPNETCORE_ENVIRONMENT
Remove-Item env:Database__Provider
Remove-Item env:Database__ConnectionString
```

**Step 5 — Verify migrations generated**

```powershell
Get-ChildItem src\Infrastructure\ToolEngine.Infrastructure\Persistence\Migrations\
# Should show: InitialCreate.cs, AddScenarioExecution.cs, AppDbContextModelSnapshot.cs
```

> Migrations run automatically on Lambda cold start — `db.Database.MigrateAsync()` is called in `Program.cs` on startup. A self-healing block detects stale SQLite-typed columns and drops/recreates them before migrating.

- [ ] Existing migrations deleted
- [ ] `InitialCreate` regenerated (Postgres types)
- [ ] `AddScenarioExecution` regenerated
- [ ] `dotnet build` still passes: `dotnet build ToolEngine.sln --configuration Release`

---

### Final Build Verification

```powershell
cd "D:\WorkingFolder\ONEBCG v2026\paymentprocessingpoc\code"
dotnet build ToolEngine.sln --configuration Release --no-incremental
# Expected: Build succeeded. 0 Warning(s). 0 Error(s).
```

- [ ] Full solution builds with 0 errors, 0 warnings

---

## 4. Build & Deploy

### 4.1 Deploy API Lambda

```powershell
cd "D:\WorkingFolder\ONEBCG v2026\paymentprocessingpoc\code\src\Hosts\ToolEngine.Api"
dotnet lambda deploy-function ToolEngine-Api
```

The tool will:
1. Publish the project (`dotnet publish -c Release -r linux-x64`)
2. Package into a zip and upload to S3 (`toolengine-lambda-deploys-515674010661`)
3. Create or update the Lambda function `ToolEngine-Api`
4. Set environment variables
5. Create the Function URL with RESPONSE_STREAM mode

**Expected output at the end:**

```
New Lambda URL endpoint created: https://XXXXXXXXXX.lambda-url.ap-south-1.on.aws/
```

**Actual deployed URL:**
```
API Lambda URL = https://vsyn67ytbwgdb3ysakfgraflvy0mwrts.lambda-url.ap-south-1.on.aws/
```

First deployment takes ~3 minutes. Subsequent deployments ~90 seconds.

- [ ] API Lambda deployed successfully
- [ ] API Lambda Function URL recorded

---

### 4.2 Deploy UI Lambda

**First — update the UI config with the API URL**

Edit `src/Hosts/ToolEngine.Ui/appsettings.Production.json`:

```json
{
  "ApiBaseUrl": "https://vsyn67ytbwgdb3ysakfgraflvy0mwrts.lambda-url.ap-south-1.on.aws"
}
```

**Then deploy:**

```powershell
cd "D:\WorkingFolder\ONEBCG v2026\paymentprocessingpoc\code\src\Hosts\ToolEngine.Ui"
dotnet lambda deploy-function ToolEngine-Ui
```

**Actual deployed URL:**
```
UI Lambda URL = https://jxcd4ft6mcth32v5gbxc6efevm0lkolz.lambda-url.ap-south-1.on.aws/
```

- [ ] `appsettings.Production.json` updated with real API URL
- [ ] UI Lambda deployed successfully
- [ ] UI Lambda Function URL recorded

---

## 5. Post-Deploy Configuration

### 5.1 Update API CORS with UI URL

Now that both Lambda URLs are known, update the API's CORS setting without a full redeploy:

```powershell
aws lambda update-function-configuration --function-name ToolEngine-Api --environment "Variables={ASPNETCORE_ENVIRONMENT=Production,AWS_DEFAULT_REGION=ap-south-1,Cors__AllowedOrigins=https://jxcd4ft6mcth32v5gbxc6efevm0lkolz.lambda-url.ap-south-1.on.aws}" --region ap-south-1
```

Wait ~10 seconds for the configuration update to propagate.

- [ ] CORS env var updated with real UI Lambda URL

### 5.2 Verify Lambda configuration

```powershell
# Check API function state and config
aws lambda get-function --function-name ToolEngine-Api --region ap-south-1 --query "Configuration.{State:State,Runtime:Runtime,Memory:MemorySize,Timeout:Timeout}"

# Check Function URL invoke mode
aws lambda get-function-url-config --function-name ToolEngine-Api --region ap-south-1 --query "{URL:FunctionUrl,InvokeMode:InvokeMode}"
# Expected: InvokeMode = RESPONSE_STREAM

# Check environment variables
aws lambda get-function-configuration --function-name ToolEngine-Api --region ap-south-1 --query "Environment.Variables"
```

- [ ] API Lambda state: `Active`
- [ ] API Function URL invoke mode: `RESPONSE_STREAM`
- [ ] Environment variables contain `Cors__AllowedOrigins`

---

## 6. Verification Checklist

Run these in sequence against the deployed API Lambda URL. All should succeed before calling the deployment complete.

**Base URL variable (PowerShell):**
```powershell
$API = "https://vsyn67ytbwgdb3ysakfgraflvy0mwrts.lambda-url.ap-south-1.on.aws"
$UI  = "https://jxcd4ft6mcth32v5gbxc6efevm0lkolz.lambda-url.ap-south-1.on.aws"
```

### 6.1 Health check

```powershell
curl "$API/health"
```

Expected response:
```json
{ "status": "healthy", "version": "v2026-poc", "utcNow": "..." }
```

- [ ] Health check returns 200 + `"status":"healthy"`

### 6.2 Dev token

```powershell
$TOKEN = (curl -s -X POST "$API/dev/token" -H "Content-Type: application/json" -d '{"userId":"test-user","userName":"Test User"}' | ConvertFrom-Json).token
echo $TOKEN
```

Expected: JWT token starting with `eyJ`.

- [ ] Token returned (non-empty, starts with `eyJ`)

### 6.3 Tool registry

```powershell
curl "$API/api/v1/tools" -H "Authorization: Bearer $TOKEN"
```

Expected: JSON array of 10 payment tools (`payment.initiate`, `payment.verify-payee`, etc.)

- [ ] Tools list returns 10 tools

### 6.4 Database connectivity (payment initiation)

```powershell
curl -X POST "$API/api/v1/payments" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"payerName":"ONE BCG UK Ltd","payerJurisdiction":"GB","payerEntityId":"PAYER-ONEBCG-001","payeeRef":"Acme Consulting","grossAmount":5000,"currency":"GBP","serviceType":0,"ppmId":"PPM-001"}'
```

Expected: HTTP 202 with `prid` and `pendingApprovalId` (payment suspended at Stage 5 approval gate).

- [ ] Payment initiation returns 202 with PRID

### 6.5 UI loads

Open in browser: `https://jxcd4ft6mcth32v5gbxc6efevm0lkolz.lambda-url.ap-south-1.on.aws/`

Expected: ONE BCG ToolEngine UI loads with dark navy topbar and sidebar navigation.

- [ ] UI loads in browser
- [ ] "Authenticated" shows in right panel after JWT acquired

### 6.6 UI config endpoint

```powershell
curl "$UI/config"
```

Expected (three fields — consumed by the UI on load):
```json
{
  "apiBaseUrl":     "https://vsyn67ytbwgdb3ysakfgraflvy0mwrts.lambda-url.ap-south-1.on.aws",
  "googleClientId": "314187099083-u5j430ki6clqof53v9p218aq8rkocc9n.apps.googleusercontent.com",
  "allowedDomain":  "onebcg.com"
}
```

`googleClientId` and `allowedDomain` are not secrets — they are read by browser JS to initialise Google Identity Services and display the domain restriction message.

- [ ] Config returns correct API URL
- [ ] `googleClientId` and `allowedDomain` present

### 6.7 SSE chat streaming

```powershell
curl -N -X POST "$API/api/v1/chat/stream" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"message":"What payment tools do you have?"}' --max-time 60
```

Expected: SSE events streamed, ending with `event: done`.

- [ ] SSE streaming events received progressively (not all at once)

### 6.8 CloudWatch Logs

```powershell
aws logs describe-log-groups --log-group-name-prefix /aws/lambda/ToolEngine --region ap-south-1 --query "logGroups[].logGroupName"
# Expected: ["/aws/lambda/ToolEngine-Api", "/aws/lambda/ToolEngine-Ui"]

# Tail API logs
aws logs tail /aws/lambda/ToolEngine-Api --region ap-south-1 --since 5m
```

- [ ] Log groups exist for both Lambdas
- [ ] Logs show startup and request entries

---

## 7. Ongoing Operations

### Redeploy after code changes

```powershell
cd "D:\WorkingFolder\ONEBCG v2026\paymentprocessingpoc\code"

# Build first to catch errors
dotnet build ToolEngine.sln --configuration Release

# Redeploy API
cd src\Hosts\ToolEngine.Api
dotnet lambda deploy-function ToolEngine-Api

# Redeploy UI (if UI changed)
cd ..\ToolEngine.Ui
dotnet lambda deploy-function ToolEngine-Ui
```

Estimated time per redeploy: ~90 seconds.

### Update a secret

```powershell
aws secretsmanager update-secret --secret-id "toolengine/anthropic-api-key" --secret-string "sk-ant-new-key-here" --region ap-south-1

# After updating a secret, Lambda reads it on next cold start.
# Force a cold start by touching any env var:
aws lambda update-function-configuration --function-name ToolEngine-Api --description "Force cold start" --region ap-south-1
```

### Switch LLM provider without redeploy

```powershell
# Switch to Gemini
aws lambda update-function-configuration --function-name ToolEngine-Api --environment "Variables={ASPNETCORE_ENVIRONMENT=Production,AWS_DEFAULT_REGION=ap-south-1,LLM__Provider=gemini}" --region ap-south-1

# Switch back to Claude
aws lambda update-function-configuration --function-name ToolEngine-Api --environment "Variables={ASPNETCORE_ENVIRONMENT=Production,AWS_DEFAULT_REGION=ap-south-1,LLM__Provider=claude}" --region ap-south-1
```

### View live logs

```powershell
# API logs
aws logs tail /aws/lambda/ToolEngine-Api --follow --region ap-south-1

# UI logs
aws logs tail /aws/lambda/ToolEngine-Ui --follow --region ap-south-1
```

### Check Lambda invocation metrics

```powershell
aws cloudwatch get-metric-statistics --namespace AWS/Lambda --metric-name Invocations --dimensions Name=FunctionName,Value=ToolEngine-Api --start-time (Get-Date).AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss") --end-time (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss") --period 3600 --statistics Sum --region ap-south-1
```

### Stop incurring costs (pause POC)

Lambda: No charges when idle — nothing to stop.

RDS: To pause (avoid storage charges beyond free tier):

```powershell
# Stop RDS (pauses billing — restarts automatically after 7 days)
aws rds stop-db-instance --db-instance-identifier toolengine-poc --region ap-south-1

# Restart when needed
aws rds start-db-instance --db-instance-identifier toolengine-poc --region ap-south-1
```

---

## 8. Cost Reference

| Service | Free tier | Post free tier |
|---------|-----------|----------------|
| API Lambda (invocations + GB-sec) | 1M requests + 400k GB-sec/mo free forever | ~$0.20/mo at POC usage |
| UI Lambda | Included in free tier | ~$0.05/mo |
| RDS db.t3.micro PostgreSQL | 750 hrs/mo (yr 1) | ~$13/mo |
| Secrets Manager (4 secrets) | None | $1.60/mo |
| CloudWatch Logs | 5 GB/mo free | ~$0/mo |
| Lambda Function URLs | Free | $0 |
| API Gateway HTTP API | 1M calls/mo free (yr 1) | ~$1/mo |
| **Total** | **~$1.60/mo** | **~$15/mo** |

> Free tier RDS applies only to the first 12 months of a new AWS account.

---

## 9. Rollback

### Lambda rollback to previous version

```powershell
# List available versions
aws lambda list-versions-by-function --function-name ToolEngine-Api --query "Versions[].{Version:Version,Modified:LastModified}"

# Rollback to a specific version (e.g. version 3)
aws lambda update-alias --function-name ToolEngine-Api --name live --function-version 3 --region ap-south-1
```

> If aliases are not configured, rollback requires redeploying an older build. Recommended: tag releases in version control before each deploy.

### Database rollback

```powershell
# List available automated snapshots
aws rds describe-db-snapshots --db-instance-identifier toolengine-poc --query "DBSnapshots[].{Id:DBSnapshotIdentifier,Time:SnapshotCreateTime,Status:Status}" --region ap-south-1

# Restore to a snapshot creates a new RDS instance
# Then update the db-connection secret with the new endpoint
```

### Full teardown (delete all resources)

```powershell
# Delete Lambda functions
aws lambda delete-function --function-name ToolEngine-Api --region ap-south-1
aws lambda delete-function --function-name ToolEngine-Ui --region ap-south-1

# Delete RDS (creates final snapshot by default)
aws rds delete-db-instance --db-instance-identifier toolengine-poc --skip-final-snapshot --region ap-south-1

# Delete secrets
aws secretsmanager delete-secret --secret-id toolengine/jwt-secret --region ap-south-1
aws secretsmanager delete-secret --secret-id toolengine/anthropic-api-key --region ap-south-1
aws secretsmanager delete-secret --secret-id toolengine/google-api-key --region ap-south-1
aws secretsmanager delete-secret --secret-id toolengine/db-connection --region ap-south-1

# Delete IAM role
aws iam detach-role-policy --role-name toolengine-lambda-role --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole
aws iam detach-role-policy --role-name toolengine-lambda-role --policy-arn arn:aws:iam::aws:policy/SecretsManagerReadWrite
aws iam delete-role --role-name toolengine-lambda-role

# Delete security groups
aws ec2 delete-security-group --group-name toolengine-lambda-sg --region ap-south-1
aws ec2 delete-security-group --group-name toolengine-rds-sg --region ap-south-1
```

---

## Progress Tracker

| Phase | Section | Status |
|-------|---------|--------|
| Prereqs | §1 | ✅ |
| IAM & CLI | §2.1 | ✅ |
| Security Groups | §2.2 | ✅ |
| RDS | §2.3 | ✅ |
| Secrets Manager | §2.4 | ✅ |
| Lambda Role | §2.5 | ✅ |
| NuGet Packages | §3.1 | ✅ |
| API Program.cs | §3.2 | ✅ |
| API appsettings | §3.3 | ✅ |
| API lambda-tools | §3.4 | ✅ |
| UI Program.cs | §3.5 | ✅ |
| UI appsettings | §3.6 | ✅ |
| UI lambda-tools | §3.7 | ✅ |
| EF Migrations | §3.8 | ✅ |
| Build verify | §3 end | ✅ |
| Deploy API | §4.1 | ✅ |
| Deploy UI | §4.2 | ✅ |
| CORS config | §5.1 | ✅ |
| Health check | §6.1 | ⬜ |
| Dev token | §6.2 | ⬜ |
| Tool registry | §6.3 | ⬜ |
| DB connectivity | §6.4 | ⬜ |
| UI loads | §6.5 | ⬜ |
| Config endpoint | §6.6 | ⬜ |
| SSE streaming | §6.7 | ⬜ |
| CloudWatch | §6.8 | ⬜ |

---

*ONE BCG ToolEngine v2026 — AWS Lambda Deployment Runbook*
*Confidential — Internal Use Only*
