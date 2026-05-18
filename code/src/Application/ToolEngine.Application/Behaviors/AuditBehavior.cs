namespace ToolEngine.Application.Behaviors;

using System.Diagnostics;
using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Application.Telemetry;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Innermost pipeline behavior. Creates a ToolInvocationRecord before the handler runs
/// and marks it succeeded or failed after.
///
/// Also emits W3C-compatible OpenTelemetry spans (G1) and duration metrics (G2):
///   Span:    "tool.execute" — tagged with tool.fullName, tenant.id, tool.version
///   Metric:  tool.invocation.duration (histogram, ms)
///   Metric:  tool.invocation.count (counter)
///
/// H1: emits one ToolInvocationEvent row per lifecycle transition to the append-only
///     audit event table (INSERT-only on DB user — no UPDATE/DELETE).
/// H2: sets RetainUntil = InvokedAt + 90 days on every ToolInvocationRecord.
/// H4: propagates CallerType from command to both ToolInvocationRecord and events.
/// H5: propagates GovernanceMetadataJson from command to both record and events.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork                                  _uow;
    private readonly IRepository<ToolInvocationRecord, Guid>     _auditRepo;
    private readonly IRepository<ToolInvocationEvent, Guid>      _eventRepo;
    private readonly IDateTimeProvider                            _clock;

    public AuditBehavior(
        IUnitOfWork                                  uow,
        IRepository<ToolInvocationRecord, Guid>     auditRepo,
        IRepository<ToolInvocationEvent, Guid>      eventRepo,
        IDateTimeProvider                            clock)
    {
        _uow       = uow;
        _auditRepo = auditRepo;
        _eventRepo = eventRepo;
        _clock     = clock;
    }

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var toolFullName = $"{cmd.ToolNamespace}.{cmd.ToolName}";

        // G1 — Start a child span. Parent span is set automatically by OTel context propagation.
        using var activity = ToolEngineTelemetry.ActivitySource.StartActivity("tool.execute");
        activity?.SetTag("tool.fullName",  toolFullName);
        activity?.SetTag("tool.version",   cmd.ToolVersion);
        activity?.SetTag("tenant.id",      cmd.TenantId);
        activity?.SetTag("correlation.id", cmd.CorrelationId.ToString());

        var record = ToolInvocationRecord.Create(
            cmd.CorrelationId, cmd.TenantId, cmd.UserId,
            cmd.ToolNamespace, cmd.ToolName, cmd.ToolVersion, cmd.ToolType, _clock,
            callerType:             cmd.CallerType,             // H4
            governanceMetadataJson: cmd.GovernanceMetadataJson, // H5
            retentionDays:          90);                        // H2

        await _auditRepo.AddAsync(record, ct);

        // H1 — Invoked event.
        await EmitEventAsync(record, InvocationEventType.Invoked, cmd, ct: ct);

        record.MarkRunning();
        await _uow.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        TResponse response;

        try
        {
            response = await next();
        }
        catch (Exception ex)
        {
            sw.Stop();
            record.MarkFailed(ToolError.Internal(ex.Message), _clock);
            record.ClearDomainEvents();

            // H1 — Failed event (exception path).
            await EmitEventAsync(record, InvocationEventType.Failed, cmd,
                durationMs:   sw.Elapsed.TotalMilliseconds,
                errorCode:    "EXCEPTION",
                errorMessage: ex.Message,
                ct:           ct);

            await _uow.SaveChangesAsync(ct);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("invocation.status", "failed");

            RecordMetrics(toolFullName, cmd.TenantId, "failed", sw.Elapsed.TotalMilliseconds);
            throw;
        }

        sw.Stop();

        string status;
        if (response is IToolResponse toolResponse)
        {
            if (toolResponse.Success)
            {
                record.MarkSucceeded(toolResponse.Metrics, _clock);
                status = "succeeded";
                // H1 — Succeeded event.
                await EmitEventAsync(record, InvocationEventType.Succeeded, cmd,
                    durationMs: sw.Elapsed.TotalMilliseconds, ct: ct);
            }
            else if (toolResponse.PendingInvocationId.HasValue)
            {
                // C3 — Suspended: awaiting human approval. Must NOT be persisted as Failed.
                // MarkSuspended leaves CompletedAt null; the record will be updated to
                // Succeeded/Failed when the approval resolves and the tool re-executes.
                record.MarkSuspended();
                status = "suspended";
                activity?.SetTag("approval.invocationId", toolResponse.PendingInvocationId.ToString());
                // H1 — Suspended event.
                await EmitEventAsync(record, InvocationEventType.Suspended, cmd,
                    durationMs: sw.Elapsed.TotalMilliseconds, ct: ct);
            }
            else
            {
                record.MarkFailed(toolResponse.Error!, _clock);
                status = "failed";
                activity?.SetStatus(ActivityStatusCode.Error, toolResponse.Error?.Description);
                // H1 — Failed event (controlled failure path).
                await EmitEventAsync(record, InvocationEventType.Failed, cmd,
                    durationMs:   sw.Elapsed.TotalMilliseconds,
                    errorCode:    toolResponse.Error?.Code,
                    errorMessage: toolResponse.Error?.Description,
                    ct:           ct);
            }
        }
        else
        {
            status = "succeeded";
            // H1 — Succeeded event (non-ToolResponse handler).
            await EmitEventAsync(record, InvocationEventType.Succeeded, cmd,
                durationMs: sw.Elapsed.TotalMilliseconds, ct: ct);
        }

        activity?.SetTag("invocation.status", status);

        record.ClearDomainEvents();
        await _uow.SaveChangesAsync(ct);

        RecordMetrics(toolFullName, cmd.TenantId, status, sw.Elapsed.TotalMilliseconds);

        return response;
    }

    /// <summary>
    /// H1 — Creates and persists one ToolInvocationEvent row.
    /// The write is batched into the same SaveChangesAsync call as the record mutation.
    /// </summary>
    private async Task EmitEventAsync(
        ToolInvocationRecord record,
        InvocationEventType  eventType,
        IExecuteToolCommand  cmd,
        double?              durationMs   = null,
        string?              errorCode    = null,
        string?              errorMessage = null,
        CancellationToken    ct           = default)
    {
        var ev = ToolInvocationEvent.Create(
            invocationRecordId:    record.Id,
            correlationId:         cmd.CorrelationId,
            tenantId:              cmd.TenantId,
            userId:                cmd.UserId,
            callerType:            cmd.CallerType,             // H4
            toolNamespace:         cmd.ToolNamespace,
            toolName:              cmd.ToolName,
            toolVersion:           cmd.ToolVersion,
            eventType:             eventType,
            durationMs:            durationMs,
            errorCode:             errorCode,
            errorMessage:          errorMessage,
            governanceMetadataJson: cmd.GovernanceMetadataJson); // H5
        await _eventRepo.AddAsync(ev, ct);
    }

    private static void RecordMetrics(
        string toolFullName, string tenantId, string status, double durationMs)
    {
        var tags = new TagList
        {
            { "tool.fullName",       toolFullName },
            { "tenant.id",           tenantId     },
            { "invocation.status",   status       }
        };
        ToolEngineTelemetry.ToolInvocationDuration.Record(durationMs, tags);
        ToolEngineTelemetry.ToolInvocationCount.Add(1, tags);
    }
}
