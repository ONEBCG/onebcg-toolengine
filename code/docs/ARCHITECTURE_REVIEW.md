# ToolEngine — Architecture Review

**ONE BCG** | ToolEngine v2026 | Industry Pattern Assessment

---

## Method

This review cross-references the ToolEngine implementation against patterns from Google, Microsoft, AWS, Stripe, GitHub, Netflix, OpenAI, Anthropic, and the following standards:

- NIST AI RMF / NISTIR 8596 Cyber AI Profile (Dec 2025)
- NIST AI Agent Identity & Authorization Concept Paper (Feb 2026)
- EU AI Act Articles 9–17, 26 (enforcement: August 2, 2026)
- OWASP Top 10 2025, OWASP Secrets Management, OWASP MFA Cheat Sheet
- W3C TraceContext / OpenTelemetry specification
- ISO 42001 (AI Management Systems, 2024)
- SOC 2 Type II CC6 / CC7 controls
- Microsoft Azure Async Request-Reply pattern (learn.microsoft.com)
- AWS Prescriptive Guidance — Multi-tenant API Access Control

Findings are rated: **Critical** (security/compliance risk), **High** (reliability/scalability gap), **Medium** (observability/operations), **Low** (optimisation opportunity).

---

## Summary scorecard

| Area | Rating | Verdict |
|---|---|---|
| Human-in-the-loop / NIST alignment | ✅ Strong | Pattern-correct; agent identity gap |
| Multi-tenancy | ⚠️ Partial | Namespace scoping present; enforcement model weak |
| Async approval / 202 pattern | ✅ Good | RFC compliance (E6), idempotency (F8), outbox delivery (F7) resolved |
| MediatR pipeline | ✅ Good | Auth order (E4), daily budget (E5), tenant caching (F5), deny-by-default (F6) resolved |
| Repository / persistence | ✅ Good | Pagination (F9), migrations (F2), provider routing (F1) resolved |
| Security | ⚠️ Improved | Phase E resolves all Critical/High security gaps — remaining: PII masking, distributed hardening |
| Observability | ✅ Good | OTel tracing + metrics + W3C TraceContext + PII masking implemented (Phase G) |
| Rate limiting / circuit breaking | ✅ Good | Daily budget (E5), OTP rate-limit (E3), loop detection distributed (F4) resolved |
| Scalability | ⚠️ Partial | Stateful in DB; no outbox; no durable resume |
| Compliance (SOC 2, GDPR, EU AI Act) | ✅ Good | Append-only event log (H1), GDPR RetainUntil + Anonymize (H2), EU AI Act Article 14 acknowledgement (H3), CallerType agent identity (H4), ISO 42001 governance metadata (H5) |

---

## 1. Human-in-the-loop / AI Governance

### What matches

- Risk-tiered `[RequiresApproval]` with four tiers (Low / Medium / High / Critical) aligns directly with the NIST Cyber AI Profile Dec 2025 requirement that "irreversible or high-impact AI actions must have a deterministic control outside the agent reasoning loop."
- `ApprovalBehavior` intercepts before execution, not after — correct placement per NIST AI Agent RFI 2025-0035 guidance on pre-action approval.
- All four approval channels (Dashboard, EmailMagicLink, EmailOtp, Webhook) are present and each provides a human decision path — satisfying EU AI Act Article 14's requirement that high-risk AI systems "can be effectively overseen by natural persons."
- `ToolInvocationRecord` captures full lifecycle (Pending → Running → Succeeded/Failed) including who requested, what tool, which version, which tenant, when.
- OTP forced for Critical risk regardless of tenant configuration is the correct approach; NIST SP 800-63B Level 3 aligns with OTP for high-stakes actions.

### Gaps

**✅ [Critical — Resolved H4] No agent identity claims.**
`CallerType` enum (`Human` / `AiAgent` / `SystemService`) is sourced from the JWT claim `"caller_type"`. It is propagated through `IExecuteToolCommand` → `ExecuteToolCommand` → `AuditBehavior`, and persisted on both `ToolInvocationRecord` and every `ToolInvocationEvent` row. `ToolEndpoints` maps the claim at the API boundary with `Human` as the safe default. Dashboards can now filter AI-generated actions from human-generated ones.

**✅ [Medium — Resolved H3] EU AI Act Article 14 evidence gap.**
`AsyncApprovalGate` now generates a structured `AcknowledgementStatement` for `High` and `Critical` risk tools and serialises it to `PendingApproval.AcknowledgementJson`. The statement records: regulatory basis, risk level, tool full name, operator statement, and issued-at timestamp. This creates a verifiable evidence trail that the approver was informed of the risk classification before the approval was granted.

**[Low] No approval escalation path.**
If the approver does not respond within the timeout window, the approval simply expires. No escalation to a secondary approver or on-call rotation is triggered. For production AI systems, silent expiry can block business processes.

*Recommendation:* Add `EscalationEmail` and `EscalationThresholdMinutes` to `ApprovalOptions`. At the halfway point of the timeout, re-notify or escalate.

---

## 2. Multi-tenancy

### What matches

- `TenantId` is present on every command, entity, approval, and invocation record.
- `TenantAuthorizationBehavior` validates the tenant is active and namespace is allowed before any business logic executes.
- Namespace allowlist with empty = unrestricted mirrors the flexibility-first approach used by AWS's SaaS tenant isolation guidance.
- `MaxResponseTokens` and `DailyToolCallBudget` are defined on the `Tenant` entity.

### Gaps

**✅ [High — Resolved E5] `DailyToolCallBudget` is defined but never enforced.**
`DailyBudgetBehavior<TRequest, TResponse>` is now registered in the pipeline between `TokenBudgetBehavior` and `LoopDetectionBehavior`. It issues a `COUNT(*)` on `ToolInvocationRecord` for the tenant filtered by `InvokedAt >= startOfDayUtc`. Returns `429 DAILY_BUDGET_EXCEEDED` when the cap is reached. `DailyToolCallBudget == 0` means no cap. Phase F will replace the `COUNT(*)` with a Redis `INCR` counter for O(1) distributed enforcement.

**[High] Namespace restriction is opt-out, not opt-in.**
`AllowedNamespaces.Count == 0` means all namespaces are permitted. AWS Prescriptive Guidance for SaaS multi-tenant API access control recommends deny-by-default — tenants should explicitly grant access to each namespace they need. The current model means a newly created tenant has access to everything, including `hr.update-employee` and `finance.process-payment`.

*Recommendation:* Invert the logic: `AllowedNamespaces.Count == 0` means no namespaces allowed. Require explicit `AllowNamespace("*")` for unrestricted access, and seed dev tenants with wildcard. Change `TenantAuthorizationBehavior` accordingly.

**[Medium] No Row Level Security or tenant-scoped encryption.**
All tenants share the same EF DbContext and database tables. Cross-tenant data leakage is prevented only by application-layer filtering. If there is a bug in `TenantAuthorizationBehavior` or a query uses the wrong `TenantId`, data from another tenant is exposed.

*Recommendation:* Add PostgreSQL RLS policies (standard 2025 approach) or SQL Server row-level security on `ToolInvocationRecord` and `PendingApproval` tables, using a `SET LOCAL app.tenant_id = 'x'` session variable populated by a DB interceptor. This provides defence-in-depth independent of application logic.

---

## 3. Async approval / 202 pattern

### What matches

- HTTP 202 Accepted is returned when execution is suspended — correct per Microsoft Azure Async Request-Reply pattern and RFC 7231.
- `GET /invocations/{id}/status` provides the client poll endpoint.
- Webhook channel supports push notification to external systems.
- Magic-link and OTP provide out-of-band approval channels.

### Gaps

**✅ [High — Resolved E6] 202 response is missing RFC-required `Location` header.**
`ToolEndpoints.cs` now calls `Results.Accepted(pollUrl, body)` — the `location` parameter sets the `Location` response header automatically. `Retry-After: 10` is also set explicitly before the return. Both headers are now present on every 202 response.

**✅ [High — Resolved F8] No idempotency key on invoke endpoint.**
`Idempotency-Key` header is now read by `ToolEndpoints` and propagated through `ApprovalContext` to `AsyncApprovalGate`. If an existing non-expired `PendingApproval` matches the key + tenant + tool, the gate returns it without creating a duplicate.

**[Medium] No automatic re-execution after approval.**
When an approver approves, the approval state transitions to `Approved`, but the tool does not re-execute. The client must re-send the full `POST /tools/...` request. This creates a poor experience and a reliability gap — the original request context (headers, input payload) must be preserved by the client. Industry standard (Stripe, Temporal) is durable execution where the workflow resumes from the suspension point.

*Recommendation:* Store the full serialized command in `PendingApproval.SerializedInput`. On approval via `/approvals/{token}/decide`, enqueue a background job (via `IBackgroundJobClient` or a hosted service) that re-dispatches the command through MediatR. Return the execution result in the status poll once complete.

---

## 4. MediatR pipeline

### What matches

- Six-layer pipeline with clear separation of concerns.
- Validation as outermost guard is correct per .NET clean architecture guidance.
- Audit as innermost (closest to handler) is the canonical pattern — Microsoft's DDD microservices guide places infrastructure concerns innermost.
- Loop detection and token budget are independent, composable guards.

### Gaps

**✅ [High — Resolved E4] Auth runs after Validation — leaks error details to unauthorized callers.**
Pipeline order corrected to: `TenantAuthorizationBehavior → ValidationBehavior → TokenBudgetBehavior → DailyBudgetBehavior → LoopDetectionBehavior → ApprovalBehavior → AuditBehavior`. Unauthorized callers now receive `401 UNAUTHORIZED` before any field-level validation details are disclosed.

**✅ [High — Resolved F5] TenantAuthorizationBehavior and TokenBudgetBehavior each load Tenant independently.**
`CachedTenantReadRepository` (scoped decorator) caches the Tenant in a `Dictionary<string, Tenant?>` for the lifetime of the HTTP request. First call hits DB; subsequent calls return cached value. Zero duplicate DB reads per pipeline pass.

**✅ [Medium — Resolved F6] Namespace restriction is opt-out, not opt-in.**
Logic inverted to deny-by-default. `AllowedNamespaces.Count == 0` means no access. `"*"` means unrestricted. All existing dev tenants should have `AllowedNamespaces = ["*"]`.

**[High] TenantAuthorizationBehavior and TokenBudgetBehavior each load Tenant independently.**
Two separate `IReadRepository.GetByIdAsync` calls per request for the same tenant record. For a high-throughput API this doubles DB reads on the hot path.

*Recommendation:* Introduce a scoped `TenantContext` service (similar to `ICurrentUser`) that caches the loaded Tenant for the duration of the HTTP request:
```csharp
public interface ITenantContext
{
    Task<Tenant?> GetCurrentAsync(string tenantId, CancellationToken ct);
}
// Backed by a scoped cache — loads once, shared across behaviors.
```

**[Medium] No `TransactionBehavior`.**
`AuditBehavior` writes a `ToolInvocationRecord` before the handler, then updates it after. If the process crashes between the two writes, the record is stuck in `Running` status forever. There is no compensation or cleanup path.

*Recommendation:* Wrap the handler + post-handler save in a database transaction scope within `AuditBehavior`. Alternatively, add a `StalledInvocationSweep` background service that marks `Running` records older than a threshold as `Failed`.

**[Medium] `LoopDetectionBehavior` memory leak.**
`static readonly ConcurrentDictionary<string, int>` accumulates entries indefinitely. A process running for 24+ hours handling thousands of distinct correlations will consume growing memory with no release path.

*Recommendation:* Replace the static dictionary with `IMemoryCache` (ASP.NET Core built-in) with a sliding expiration tied to the expected agent turn duration (e.g., 10 minutes):
```csharp
_cache.GetOrCreate(key, entry => {
    entry.SlidingExpiration = TimeSpan.FromMinutes(10);
    return 0;
});
```

---

## 5. Repository / persistence

### What matches

- `IRepository<T, TId>` / `IReadRepository<T, TId>` separation is correct and aligns with CQRS read/write path separation.
- `AsNoTracking()` on the read repository is the standard EF Core performance recommendation.
- `LambdaSpecification<T>` provides inline queries without ceremony.
- `IUnitOfWork` correctly scopes the transaction.

### Gaps

**✅ [High — Resolved F2] No EF Core migrations — `EnsureCreated()` only.**
`Program.cs` now calls `db.Database.MigrateAsync()` in production and `EnsureCreated()` in development only. Migration commands:
```bash
dotnet ef migrations add InitialCreate \
  --project src/Infrastructure/ToolEngine.Infrastructure \
  --startup-project src/Hosts/ToolEngine.Api
```

**✅ [High — Resolved F9] `ListAllAsync()` has no pagination.**
`IReadRepository<TEntity, TId>` now includes `PagedListAsync(spec, pageNumber, pageSize, ct)` returning `PagedResult<T>` with `TotalCount`, `TotalPages`, `HasNext`, `HasPrevious`. `ListAllAsync` remains for admin use — callers must apply their own limits.

**✅ [Medium — Resolved H2] No soft-delete or GDPR anonymization strategy.**
`ToolInvocationRecord` now carries `RetainUntil` (populated at creation as `InvokedAt + 90 days`) and `IsAnonymized`. `Anonymize()` nulls `UserId → "[anonymized]"`, `ErrorMessage`, and `GovernanceMetadataJson`, then sets `IsAnonymized = true`. An index on `(RetainUntil, IsAnonymized)` supports the retention sweep query. The structural record (tool called, status, timestamps) is retained for SOC 2 completeness counts; only PII is removed. `ToolInvocationEvent` is exempt — it carries no PII beyond `UserId`, which is required for legal accountability (GDPR Recital 26).

---

## 6. Security

### Gaps

**✅ [Critical — Resolved E3] OTP endpoint has no rate limiting or lockout.**
`POST /approvals/otp/verify` is now protected by two independent layers:
1. **IP-level rate limit** — `SlidingWindowRateLimiter`: 10 attempts per IP per 10 minutes. Excess requests return `429` with `Retry-After: 60`.
2. **Per-token lockout** — `PendingApproval.FailedOtpAttempts` counter; after 5 failures the approval transitions to `Expired`. A new approval request is required.

**✅ [Critical — Resolved E1] Magic link token entropy below OWASP recommendation.**
`ApprovalToken` is now generated as `Convert.ToHexString(RandomNumberGenerator.GetBytes(32))` — 256-bit CSPRNG entropy, 64-character hex string. Exceeds the OWASP minimum (128 bits) by 2×.

**✅ [Critical — Resolved E4] Pipeline order: Validation before Auth leaks error messages.**
`TenantAuthorizationBehavior` is now the outermost behavior. Unauthorized callers receive `401 UNAUTHORIZED` before any `400 VALIDATION_ERROR` field detail.

**✅ [High — Resolved E7] `BaseUrl` in `ApprovalOptions` defaults to `http://`.**
`Program.cs` now fails fast with `InvalidOperationException` at startup if `Approval:BaseUrl` does not start with `https://` in non-development environments. Magic links cannot be sent over HTTP in staging or production.

**✅ [High — Resolved E2 + E3] No OTP attempt counter — token not invalidated on max failures.**
`PendingApproval` now has `int FailedOtpAttempts { get; private set; }` and `IncrementFailedOtpAttempts(int maxAttempts)`. After 5 failures the approval is expired. Remaining attempt count is surfaced in the `400` response body.

**[Medium] No PII masking in structured logs.**
`AsyncApprovalGate` logs `context.ToolFullName`, risk level, and `pending.Id`. `AuditBehavior` does not log inputs. This is acceptable. However, `WebhookChannel` logs the full webhook URL (which may contain tokens in query strings), and `EmailMagicLinkChannel` / `EmailOtpChannel` log `to` (the approver's email address). Email addresses are PII under GDPR.

*Recommendation:* Use Serilog destructuring policies to mask email addresses:
```csharp
.Destructure.ByTransforming<string>(s =>
    s.Contains('@') ? $"{s[..2]}***@***.***" : s)
```
Or use `[LogMasked]` attributes from `Serilog.Enrichers.Sensitive`.

**✅ [Medium — Resolved E7] JWT minimum key length not enforced.**
`Program.cs` now validates `Encoding.UTF8.GetBytes(jwt.Secret).Length >= 32` immediately after JWT config binding. The API will not start with a short signing key. Error message includes the `openssl rand -base64 32` generation command.

---

## 7. Observability

### Gaps

**✅ [Critical — Resolved G1] No OpenTelemetry — industry-mandatory in 2025.**
ToolEngine now emits W3C-compatible OTel spans via a custom `ActivitySource("ToolEngine")`. The OTLP exporter is configured via `"Otlp:Endpoint"`. ASP.NET Core, outbound HTTP (webhooks), and EF Core queries are instrumented automatically. `X-Correlation-Id` remains as the business correlation key and is propagated as an OTel span tag.

**✅ [High — Resolved G2/G3] No metrics.**
Six metric instruments are registered on `Meter("ToolEngine")`:
`tool.invocation.duration`, `tool.invocation.count`, `tool.approval.pending.count`, `tool.approval.wait.duration`, `tool.loop.detection.triggers`, `tool.daily.budget.exceeded`. All exported via OTLP.

**[Medium] CorrelationId vs TraceId — semantic gap.**
`CorrelationId` in ToolEngine is a business-level identifier (one agent turn = one correlation). W3C `TraceId` is a technical infrastructure identifier (one HTTP request = one trace). These are different concepts and should coexist rather than conflate. Currently there is no W3C trace context at all.

*Recommendation:* Keep `CorrelationId` as the business concept (propagate as baggage in OTel). Let the W3C `TraceId` be the technical infrastructure identifier set automatically by the OTel SDK.

---

## 8. Rate limiting / circuit breaking

### What matches

- `LoopDetectionBehavior` — per-correlation, per-tool call counter.
- `TokenBudgetBehavior` — per-invocation token cap against tenant maximum.
- `DailyToolCallBudget` defined on `Tenant`.

### Gaps

**✅ [Critical — Resolved E5] `DailyToolCallBudget` exists but is never checked.**
`DailyBudgetBehavior` now enforces this. See §2 for detail.

**[High] No global API rate limiting.**
No per-tenant requests-per-minute limit, no per-IP limit, no global burst cap. A single tenant with a runaway AI agent can flood the API.

*Recommendation:* Add ASP.NET Core built-in rate limiting middleware (available since .NET 7):
```csharp
builder.Services.AddRateLimiter(opt => {
    opt.AddSlidingWindowLimiter("per-tenant", config => {
        config.Window             = TimeSpan.FromMinutes(1);
        config.SegmentsPerWindow  = 6;
        config.PermitLimit        = 60; // 60 req/min per tenant
        config.QueueLimit         = 0;
    });
});
```
Use a policy keyed on `tenant_id` claim.

**[High] `LoopDetectionBehavior` counter never expires.**
As noted in §4: the static `ConcurrentDictionary` grows indefinitely. In a 24-hour API process with 10,000 distinct correlations per hour, this creates 240,000 entries with no eviction.

---

## 9. Scalability

### What matches

- `PendingApproval` persisted in DB — approval state survives pod restarts.
- `IApprovalChannel` abstraction allows horizontal scaling of notification delivery.
- `IHttpClientFactory` used in `WebhookChannel` — correct for connection pooling.

### Gaps

**✅ [Critical — Resolved F7] No outbox pattern for channel notifications.**
`AsyncApprovalGate` now writes `PendingApproval` + `OutboxMessage` atomically in a single `SaveChangesAsync`. `NotificationDispatchService` (IHostedService) polls every 15 seconds, delivers via `channel.SendAsync`, and retries on failure with exponential back-off (30s → 2m → 8m → 32m → 2h). After 5 failures, the message is abandoned and logged.

**✅ [High — Resolved F8] No idempotency key on invocation (duplicate approvals on retry).**
See §3.

**✅ [High — Resolved F4] LoopDetection in-process state doesn't work in horizontal scale.**
`LoopDetectionBehavior` now uses `ICacheProvider.IncrementAsync`. When `"Cache:Provider": "redis"` is configured, the counter is shared across all pods. Memory provider is used for single-node / dev deployments. Phase I replaces the optimistic GET/SET in `DistributedCacheProvider.IncrementAsync` with a true atomic Redis INCR + EXPIRE script.

**[Medium] No durable re-execution after approval.**
See §3. After an approver approves, the tool does not automatically re-execute. The client must re-POST the full request. This is acceptable for v1 but becomes a scalability and UX issue for long-lived agents. Temporal.io or Azure Durable Functions would provide a suspend/resume execution model.

---

## 10. Compliance

### What matches

- `ToolInvocationRecord` captures: who called, what tool, which tenant, when, outcome, error codes.
- `PendingApproval` captures: who approved, when, which channel.
- `DecidedByUserId` attribution on approvals — satisfies SOC 2 CC7 attribution requirement.
- `TenantId` on all records — satisfies data segregation requirement.

### Gaps

**✅ [Critical — Resolved H1] Audit record state is mutable — violates SOC 2 immutability requirement.**
`ToolInvocationEvent` (append-only) is now the authoritative audit trail. `AuditBehavior` emits one event per lifecycle transition (Invoked, Running, Succeeded, Failed, Suspended) via `EmitEventAsync`, writing to a separate `ToolInvocationEvents` table. The application DB user should be granted INSERT only on this table — enforced out-of-band in the deployment runbook. `ToolInvocationRecord` is retained for operational query convenience; the event log is the SOC 2 evidence source. The event table carries `CallerType` and `GovernanceMetadataJson` on every row.

**✅ [High — Resolved H2] No GDPR right-to-erasure strategy.**
`ToolInvocationRecord` now implements `Anonymize()` which nulls `UserId`, `ErrorMessage`, and `GovernanceMetadataJson` and sets `IsAnonymized = true`. `RetainUntil` (default `InvokedAt + 90 days`) drives the retention sweep. A background job queries `WHERE RetainUntil <= @today AND IsAnonymized = false` and anonymises in batches. The `ToolInvocationEvent` table retains `UserId` for legal accountability (GDPR Recital 26).

**✅ [High — Resolved H2] No data retention policy.**
`RetainUntil` is set on every `ToolInvocationRecord` at creation time. Default: 90 days. Index `(RetainUntil, IsAnonymized)` supports O(log n) sweep queries. SOC 2 CC6.7 evidence: records are retained for at least `RetainUntil`, anonymised after, not deleted — structural data remains for completeness counts.

**✅ [High — Resolved H3] EU AI Act Article 14 — no evidence of human comprehension.**
`AsyncApprovalGate` now generates a structured `AcknowledgementStatement` for all `High` and `Critical` risk tools. The JSON blob is persisted as `PendingApproval.AcknowledgementJson` before the notification is dispatched. It contains: regulatory basis (`"EU AI Act Article 14 §4"`), risk level, tool full name, operator statement, and issued-at timestamp. This constitutes verifiable evidence that the approval was informed.

**✅ [Medium — Resolved H4 + H5] No agent identity / ISO 42001 governance metadata.**
`CallerType` (H4) and `GovernanceMetadataJson` (H5) are propagated from JWT claim + request header through `IExecuteToolCommand` to both `ToolInvocationRecord` and every `ToolInvocationEvent` row. Downstream SIEM and compliance tooling can filter and validate these fields against the organisation's ISO 42001 control set. See §19 of IMPLEMENTATION_GUIDE.md for full detail.

---

## Priority action plan

### Immediate (before any production deployment) — Phase E complete ✅

| # | Finding | Phase | Status |
|---|---|---|---|
| 1 | Rate limiting + lockout on `POST /approvals/otp/verify` | E3 | ✅ Done |
| 2 | Replace `Guid.NewGuid()` token with `RandomNumberGenerator.GetBytes(32)` | E1 | ✅ Done |
| 3 | Reorder pipeline: TenantAuth before Validation | E4 | ✅ Done |
| 4 | Enforce `DailyToolCallBudget` in a new behavior | E5 | ✅ Done |
| 5 | Add `Location` and `Retry-After` headers to 202 response | E6 | ✅ Done |
| 6 | Add `https://` startup validation for `BaseUrl` | E7 | ✅ Done |
| 7 | Add JWT minimum key length startup validation | E7 | ✅ Done |

### Short-term (first production sprint) — Phase F complete ✅

| # | Finding | Status |
|---|---|---|
| F1 | Modular DB provider: SQLite / PostgreSQL / SQL Server via `"Database:Provider"` config | ✅ Done |
| F2 | EF Core `MigrateAsync()` in production — `EnsureCreated()` in dev only | ✅ Done |
| F3 | `ICacheProvider` abstraction — IMemoryCache or Redis via `"Cache:Provider"` config | ✅ Done |
| F4 | LoopDetection uses `ICacheProvider` (no more static dict — distributed-safe) | ✅ Done |
| F5 | `CachedTenantReadRepository` — scoped per-request tenant cache, eliminates double DB read | ✅ Done |
| F6 | Namespace allowlist inverted to deny-by-default (`"*"` = unrestricted) | ✅ Done |
| F7 | Outbox pattern: `OutboxMessage` entity + `NotificationDispatchService` with retry | ✅ Done |
| F8 | `Idempotency-Key` header — prevents duplicate `PendingApproval` on retry | ✅ Done |
| F9 | `PagedListAsync` + `PagedResult<T>` on `IReadRepository` | ✅ Done |

### Medium-term (next quarter) — Phase G complete ✅ / Phase H complete ✅

| # | Finding | Phase | Status |
|---|---|---|---|
| G1 | OpenTelemetry + W3C TraceContext — OTLP exporter, ASP.NET Core + EF instrumentation | G | ✅ Done |
| G2 | Custom ActivitySource — tool.execute + tool.approval.gate spans; 6 metric instruments | G | ✅ Done |
| G3 | OTel metrics: latency histogram, invocation count, approval pending gauge, loop triggers | G | ✅ Done |
| G4 | PII masking in Serilog — email addresses masked via destructuring policy | G | ✅ Done |
| H1 | Append-only `ToolInvocationEvent` table — immutable SOC 2 audit log | H | ✅ Done |
| H2 | GDPR anonymization sweep + `RetainUntil` retention policy | H | ✅ Done |
| H3 | EU AI Act Article 14 acknowledgement payload for High/Critical | H | ✅ Done |
| H4 | Agent identity claims in JWT (`CallerType`) + propagate through pipeline | H | ✅ Done |
| H5 | ISO 42001 AI governance metadata on `ToolInvocationRecord` | H | ✅ Done |

### Future / architectural — Phase I

| # | Finding | Phase | Effort |
|---|---|---|---|
| I1 | Redis daily budget counter — replace `COUNT(*)` in `DailyBudgetBehavior` | I | M |
| I2 | Durable re-execution after approval (Temporal / Azure Durable Functions) | I | XL |
| I3 | Row Level Security at database layer (PostgreSQL RLS) | I | L |
| I4 | Approval escalation / on-call rotation | I | M |

---

## Appendix — references

- [NIST AI Agent Standards Initiative 2026](https://www.buildmvpfast.com/blog/nist-ai-agent-standards-governance-interoperability-2026)
- [NIST AI Agent Identity & Authorization (Feb 2026)](https://www.pindrop.com/article/nist-reaction-ai-agents-need-identity-and-human-approval-needs-verification/)
- [Human-in-the-Loop AI Agents: When Approvals Matter in 2026](https://getclaw.sh/blog/human-in-the-loop-ai-agents-approvals-2026)
- [EU AI Act Article 14: Human Oversight](https://artificialintelligenceact.eu/article/14/)
- [Microsoft Azure Async Request-Reply Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/asynchronous-request-reply)
- [AWS Prescriptive Guidance — SaaS Multitenant API Access](https://docs.aws.amazon.com/prescriptive-guidance/latest/saas-multitenant-api-access-authorization/introduction.html)
- [OWASP Secrets Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Secrets_Management_Cheat_Sheet.html)
- [OWASP MFA Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multifactor_Authentication_Cheat_Sheet.html)
- [OWASP Top 10 2025 — Zero Trust](https://xage.com/blog/owasps-2025-top-10-reinforces-the-need-for-zero-trust-security/)
- [OpenTelemetry W3C Context Propagation](https://opentelemetry.io/docs/concepts/context-propagation/)
- [Idempotency Keys in REST APIs](https://zuplo.com/learning-center/implementing-idempotency-keys-in-rest-apis-a-complete-guide)
- [Compliance Frameworks for AI Infrastructure](https://introl.com/blog/compliance-frameworks-ai-infrastructure-soc2-iso27001-gdpr)
- [MediatR Pipeline Behaviors in .NET 8](https://mehmetozkaya.medium.com/mediatr-pipeline-behaviors-and-fluent-validation-in-net-8-microservices-363e3d464433)

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
