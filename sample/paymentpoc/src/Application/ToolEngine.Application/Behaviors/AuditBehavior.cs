using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;

namespace ToolEngine.Application.Behaviors;

/// <summary>
/// Behavior 7 of 8 — SOC 2 / EU AI Act Audit.
/// Creates a ToolInvocationRecord (mutable) + ToolInvocationEvent (append-only, H1).
/// H2: RetainUntil = UtcNow + 90 days.
/// H4: CallerType persisted on both record and event rows.
/// H5: GovernanceMetadataJson persisted on both rows.
/// Timing: starts Stopwatch before calling next(), records DurationMs on completion.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest  : notnull
    where TResponse : IToolResponse
{
    private readonly AppDbContext    _db;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _log;

    public AuditBehavior(
        AppDbContext db, IDateTimeProvider clock,
        ILogger<AuditBehavior<TRequest, TResponse>> log)
    {
        _db    = db;
        _clock = clock;
        _log   = log;
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        // H4/H5: extract from command
        var callerType       = cmd.CallerType;
        var governanceJson   = cmd.GovernanceMetadataJson;
        var toolFullName     = cmd.FullName;

        // Create invocation record
        var record = ToolInvocationRecord.Create(
            cmd.CorrelationId, cmd.UserId,
            toolFullName, cmd.ToolVersion,
            callerType, governanceJson, _clock);

        _db.Set<ToolInvocationRecord>().Add(record);

        // Append "Invoked" event (H1 — append-only)
        var invokedEvent = ToolInvocationEvent.Create(
            record.Id, "Invoked", callerType, governanceJson, null, _clock);
        _db.Set<ToolInvocationEvent>().Add(invokedEvent);

        await _db.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        TResponse response;
        try
        {
            response = await next();
        }
        catch (Exception ex)
        {
            sw.Stop();
            var error = ToolError.Internal(ex.Message);
            record.MarkFailed(error);
            _db.Set<ToolInvocationEvent>().Add(
                ToolInvocationEvent.Create(record.Id, "Failed", callerType, governanceJson,
                    sw.ElapsedMilliseconds, _clock));
            await _db.SaveChangesAsync(ct);
            _log.LogError(ex, "Tool '{FullName}' threw an unhandled exception.", toolFullName);
            throw;
        }

        sw.Stop();
        var metrics = new ToolUsageMetrics(sw.ElapsedMilliseconds, 0);

        if (response.IsSuspended)
        {
            record.MarkSuspended();
            _db.Set<ToolInvocationEvent>().Add(
                ToolInvocationEvent.Create(record.Id, "Suspended", callerType, governanceJson,
                    sw.ElapsedMilliseconds, _clock));
        }
        else if (response.Success)
        {
            record.MarkSucceeded(metrics);
            _db.Set<ToolInvocationEvent>().Add(
                ToolInvocationEvent.Create(record.Id, "Succeeded", callerType, governanceJson,
                    sw.ElapsedMilliseconds, _clock));
        }
        else
        {
            // Null-coalesce: a failed response should always carry an Error, but guard
            // defensively so a misconfigured handler cannot crash the audit trail.
            var auditError = response.Error
                             ?? ToolError.Internal("Tool returned failure with no error details.");
            record.MarkFailed(auditError);
            _db.Set<ToolInvocationEvent>().Add(
                ToolInvocationEvent.Create(record.Id, "Failed", callerType, governanceJson,
                    sw.ElapsedMilliseconds, _clock));
        }

        await _db.SaveChangesAsync(ct);
        return response;
    }
}
