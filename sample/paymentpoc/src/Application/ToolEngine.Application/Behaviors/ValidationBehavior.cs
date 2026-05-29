using System.Text.Json;
using FluentValidation;
using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Domain.Contracts;

namespace ToolEngine.Application.Behaviors;

/// <summary>
/// Behavior 2 of 8 — FluentValidation pipeline gate.
/// Aggregates all validation failures into a single error message.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest  : notnull
    where TResponse : IToolResponse
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) =>
        _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, ct)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        if (request is not IExecuteToolCommand cmd)
            throw new ValidationException(failures);

        var message = string.Join("; ", failures.Select(f => f.ErrorMessage));
        return Fail(cmd, ToolError.Validation(message));
    }

    private static TResponse Fail(IExecuteToolCommand cmd, ToolError error) =>
        (TResponse)(object)ToolResponse<JsonElement>.Fail(cmd.CorrelationId, error);
}
