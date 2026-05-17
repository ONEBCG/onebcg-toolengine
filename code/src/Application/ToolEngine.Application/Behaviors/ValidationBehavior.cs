namespace ToolEngine.Application.Behaviors;

using FluentValidation;
using MediatR;

/// <summary>
/// Outermost pipeline behavior. Runs all registered IValidator&lt;TRequest&gt; for the
/// incoming command. Throws ValidationException on failure — validation failures
/// do NOT create audit records (they are caught at the API boundary as HTTP 400).
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) =>
        _validators = validators;

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        var failures = _validators
            .Select(v => v.Validate(new ValidationContext<TRequest>(request)))
            .Where(r => !r.IsValid)
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
