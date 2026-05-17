namespace ToolEngine.Application.Behaviors;

using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Inner pipeline behavior. Creates a ToolInvocationRecord before the handler
/// runs and marks it succeeded or failed after. Only activates for requests that
/// implement IExecuteToolCommand — all other MediatR requests pass through.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork                              _uow;
    private readonly IRepository<ToolInvocationRecord, Guid> _auditRepo;
    private readonly IDateTimeProvider                        _clock;

    public AuditBehavior(
        IUnitOfWork                              uow,
        IRepository<ToolInvocationRecord, Guid> auditRepo,
        IDateTimeProvider                        clock)
    {
        _uow       = uow;
        _auditRepo = auditRepo;
        _clock     = clock;
    }

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var record = ToolInvocationRecord.Create(
            cmd.CorrelationId, cmd.TenantId, cmd.UserId,
            cmd.ToolNamespace, cmd.ToolName, cmd.ToolVersion, cmd.ToolType, _clock);

        await _auditRepo.AddAsync(record, ct);
        record.MarkRunning();
        await _uow.SaveChangesAsync(ct);

        TResponse response;
        try
        {
            response = await next();
        }
        catch (Exception ex)
        {
            record.MarkFailed(ToolError.Internal(ex.Message), _clock);
            record.ClearDomainEvents();
            await _uow.SaveChangesAsync(ct);
            throw;
        }

        if (response is IToolResponse toolResponse)
        {
            if (toolResponse.Success)
                record.MarkSucceeded(toolResponse.Metrics, _clock);
            else
                record.MarkFailed(toolResponse.Error!, _clock);
        }

        record.ClearDomainEvents();
        await _uow.SaveChangesAsync(ct);
        return response;
    }
}
