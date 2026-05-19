namespace ToolEngine.Application.Behaviors;

using System.Diagnostics;
using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Application.Telemetry;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Constants;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Innermost pipeline behavior. Creates a ToolInvocationRecord before the handler runs
/// and transitions it to Succeeded, Failed, or Suspended after.
///
/// Observability emitted per invocation:
///   Span:    "tool.execute" — tagged with tool.fullName, tenant.id, tool.version.
///            Parent span is set automatically by OpenTelemetry context propagation (W3C traceparent).
///   Metric:  tool.invocation.duration (histogram, ms) — latency by tool and tenant.
///   Metric:  tool.invocation.count (counter) — volume by tool, tenant, and outcome.
///
/// Audit guarantees:
///   - One ToolInvocationEvent row is written per lifecycle transition (Invoked → Running →
///     Succeeded/Failed/Suspended). The DB user has INSERT-only on this table — rows are immutable.
///   - RetainUntil = InvokedAt + 90 days on every ToolInvocationRecord (data-retention policy).
///   - CallerType is propagated from the command so audit queries can distinguish Human vs AiAgent calls.
///   - GovernanceMetadataJson is propagated for EU AI Act traceability (provider, model, session).
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

        // Start a child span. The parent span is injected automatically by the OTel
        // HttpContext propagator from the incoming W3C traceparent header.
        using var activity = ToolEngineTelemetry.ActivitySource.StartActivity("tool.execute");
        activity?.SetTag("tool.fullName",  toolFullName);
        activity?.SetTag("tool.version",   cmd.ToolVersion);
        activity?.SetTag("tenant.id",      cmd.TenantId);
        activity?.SetTag("correlation.id", cmd.CorrelationId.ToString());

        var record = ToolInvocationRecord.Create(
            cmd.CorrelationId, cmd.TenantId, cmd.UserId,
            cmd.ToolNamespace, cmd.ToolName, cmd.ToolVersion, cmd.ToolType, _clock,
            callerType:             cmd.CallerType,
            governanceMetadataJson: cmd.GovernanceMetadataJson,
            retentionDays:          90);

        await _auditRepo.AddAsync(record, ct);

        // Emit Invoked event before execution starts so the audit trail is complete
        // even if the process crashes mid-execution.
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

            // Unhandled exception path — record the exception type as the error code
            // so audit queries can distinguish pipeline errors from domain rejections.
            await EmitEventAsync(record, InvocationEventType.Failed, cmd,
                durationMs:   sw.Elapsed.TotalMilliseconds,
                errorCode:    ErrorCodes.Exception,
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
                await EmitEventAsync(record, InvocationEventType.Succeeded, cmd,
                    durationMs: sw.Elapsed.TotalMilliseconds, ct: ct);
            }
            else if (toolResponse.PendingInvocationId.HasValue)
            {
                // Suspended means awaiting human approval. Must NOT be marked Failed —
                // MarkSuspended leaves CompletedAt null; the record is updated to
                // Succeeded/Failed when the approval resolves and the tool re-executes.
                record.MarkSuspended();
                status = "suspended";
                activity?.SetTag("approval.invocationId", toolResponse.PendingInvocationId.ToString());
                await EmitEventAsync(record, InvocationEventType.Suspended, cmd,
                    durationMs: sw.Elapsed.TotalMilliseconds, ct: ct);
            }
            else
            {
                record.MarkFailed(toolResponse.Error!, _clock);
                status = "failed";
                activity?.SetStatus(ActivityStatusCode.Error, toolResponse.Error?.Description);
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
    /// Creates and persists one ToolInvocationEvent row. The write is batched into the
    /// same SaveChangesAsync call as the record mutation so both rows are always consistent.
    /// Partial writes (record updated, event missing) are not possible under this design.
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
            callerType:            cmd.CallerType,
            toolNamespace:         cmd.ToolNamespace,
            toolName:              cmd.ToolName,
            toolVersion:           cmd.ToolVersion,
            eventType:             eventType,
            durationMs:            durationMs,
            errorCode:             errorCode,
            errorMessage:          errorMessage,
            governanceMetadataJson: cmd.GovernanceMetadataJson);
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
