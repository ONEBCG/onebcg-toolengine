namespace ToolEngine.Application.Tests.Behaviors;

using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Commands;
using ToolEngine.Application.Tests.Helpers;
using ToolEngine.Core.Domain.Contracts;
using Xunit;

public sealed class ValidationBehaviorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ValidationBehavior<ExecuteToolCommand<object, object>, ToolResponse<object>> CreateBehavior(
        IEnumerable<IValidator<ExecuteToolCommand<object, object>>> validators) =>
        new(validators);

    private static ExecuteToolCommand<object, object> DefaultCommand() =>
        new CommandBuilder().Build();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoValidators_PassesThrough()
    {
        var behavior = CreateBehavior([]);
        var cmd      = DefaultCommand();
        var called   = false;

        var result = await behavior.Handle(
            cmd,
            _ => { called = true; return Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)); },
            CancellationToken.None);

        called.Should().BeTrue();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AllValidatorsPassing_PassesThrough()
    {
        var validator = new PassThroughValidator();
        var behavior  = CreateBehavior([validator]);
        var cmd       = DefaultCommand();
        var called    = false;

        var result = await behavior.Handle(
            cmd,
            _ => { called = true; return Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)); },
            CancellationToken.None);

        called.Should().BeTrue();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ValidatorFails_ThrowsValidationException()
    {
        var failure  = new ValidationFailure("ToolName", "Tool name is required.");
        var validator = new FixedFailureValidator([failure]);
        var behavior  = CreateBehavior([validator]);
        var cmd       = DefaultCommand();

        var act = () => behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Errors.Any(e => e.PropertyName == "ToolName"));
    }

    [Fact]
    public async Task MultipleValidators_AllFailures_Collected()
    {
        var failure1  = new ValidationFailure("ToolName",    "Tool name required.");
        var failure2  = new ValidationFailure("ToolVersion", "Version required.");
        var validator1 = new FixedFailureValidator([failure1]);
        var validator2 = new FixedFailureValidator([failure2]);
        var behavior   = CreateBehavior([validator1, validator2]);
        var cmd        = DefaultCommand();

        var act = () => behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().HaveCount(2);
        ex.Which.Errors.Select(e => e.PropertyName)
            .Should().Contain("ToolName")
            .And.Contain("ToolVersion");
    }

    [Fact]
    public async Task NonToolCommand_PassesThrough()
    {
        // ValidationBehavior validates ALL request types. With no validators registered
        // for string the failures list is empty and next() is called.
        var stringBehavior = new ValidationBehavior<string, ToolResponse<object>>([]);
        var called         = false;
        var correlationId  = Guid.NewGuid();

        await stringBehavior.Handle(
            "any-request",
            _ => { called = true; return Task.FromResult(ToolResponse<object>.Ok(correlationId, new object())); },
            CancellationToken.None);

        called.Should().BeTrue();
    }

    // ── Inline validator helpers ──────────────────────────────────────────────

    private sealed class PassThroughValidator
        : AbstractValidator<ExecuteToolCommand<object, object>>
    {
        // No rules — always valid.
    }

    private sealed class FixedFailureValidator
        : AbstractValidator<ExecuteToolCommand<object, object>>
    {
        private readonly IEnumerable<ValidationFailure> _failures;

        public FixedFailureValidator(IEnumerable<ValidationFailure> failures) =>
            _failures = failures;

        protected override bool PreValidate(
            ValidationContext<ExecuteToolCommand<object, object>> context,
            ValidationResult result)
        {
            foreach (var f in _failures)
                result.Errors.Add(f);
            return false; // skip rule execution
        }
    }
}
