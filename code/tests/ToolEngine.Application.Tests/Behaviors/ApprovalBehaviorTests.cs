namespace ToolEngine.Application.Tests.Behaviors;

using FluentAssertions;
using NSubstitute;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Tests.Helpers;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using Xunit;

public sealed class ApprovalBehaviorTests
{
    private readonly IToolDiscovery     _discovery = Substitute.For<IToolDiscovery>();
    private readonly IHumanApprovalGate _gate      = Substitute.For<IHumanApprovalGate>();

    private ApprovalBehavior<
        ToolEngine.Application.Commands.ExecuteToolCommand<object, object>,
        ToolResponse<object>> CreateBehavior() =>
        new(_discovery, _gate);

    // ── Descriptor factories ──────────────────────────────────────────────────

    private static Result<ToolDiscoveryDescriptor> NotFound() =>
        Result.Failure<ToolDiscoveryDescriptor>(Error.ToolNotFound("math", "calculate", "1.0"));

    private static Result<ToolDiscoveryDescriptor> WithApproval(
        bool    needsApproval  = true,
        string? approvalReason = null) =>
        Result.Success(new ToolDiscoveryDescriptor(
            Namespace:      "math",
            Name:           "calculate",
            Version:        "1.0",
            Description:    "desc",
            WhenToUse:      "always",
            WhenNotToUse:   "never",
            NeedsApproval:  needsApproval,
            ApprovalRisk:   ApprovalRisk.High,
            ApprovalReason: approvalReason));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolNotFound_PassesThrough_NoGateCall()
    {
        _discovery.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(NotFound());

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();
        var called   = false;

        var result = await behavior.Handle(
            cmd,
            _ => { called = true; return Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)); },
            CancellationToken.None);

        called.Should().BeTrue();
        await _gate.DidNotReceive()
            .RequestApprovalAsync(Arg.Any<ApprovalContext>(), Arg.Any<string>(),
                Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeedsApprovalFalse_PassesThrough_NoGateCall()
    {
        _discovery.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(WithApproval(needsApproval: false));

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();
        var called   = false;

        var result = await behavior.Handle(
            cmd,
            _ => { called = true; return Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)); },
            CancellationToken.None);

        called.Should().BeTrue();
        await _gate.DidNotReceive()
            .RequestApprovalAsync(Arg.Any<ApprovalContext>(), Arg.Any<string>(),
                Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GateAllows_PassesThrough()
    {
        _discovery.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(WithApproval());
        _gate.RequestApprovalAsync(Arg.Any<ApprovalContext>(), Arg.Any<string>(),
                Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalDecision.Allow("test-user"));

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();
        var called   = false;

        var result = await behavior.Handle(
            cmd,
            _ => { called = true; return Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)); },
            CancellationToken.None);

        called.Should().BeTrue();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GateDenies_Returns403_APPROVAL_DENIED()
    {
        _discovery.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(WithApproval());
        _gate.RequestApprovalAsync(Arg.Any<ApprovalContext>(), Arg.Any<string>(),
                Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalDecision.Deny("test-user", "Policy violation."));

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("APPROVAL_DENIED");
        result.Error.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GateSuspends_ReturnsSuspendedWithPendingId()
    {
        var pendingId = Guid.NewGuid();
        _discovery.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(WithApproval());
        _gate.RequestApprovalAsync(Arg.Any<ApprovalContext>(), Arg.Any<string>(),
                Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalDecision.Suspend(pendingId));

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.PendingInvocationId.Should().Be(pendingId);
        result.Error!.Code.Should().Be("APPROVAL_PENDING");
    }

    [Fact]
    public async Task IdempotencyKey_IsPassedToApprovalContext()
    {
        const string key = "idem-key-123";
        _discovery.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(WithApproval());

        ApprovalContext? capturedContext = null;
        _gate.RequestApprovalAsync(
                Arg.Do<ApprovalContext>(ctx => capturedContext = ctx),
                Arg.Any<string>(), Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalDecision.Allow("test-user"));

        var cmd      = new CommandBuilder().WithIdempotencyKey(key).Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            _ => Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)),
            CancellationToken.None);

        capturedContext.Should().NotBeNull();
        capturedContext!.IdempotencyKey.Should().Be(key);
    }

    [Fact]
    public async Task NullApprovalReason_UsesDefaultReason()
    {
        // Descriptor has null ApprovalReason — behavior must supply a default non-null reason
        _discovery.Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(WithApproval(approvalReason: null));

        string? capturedReason = null;
        _gate.RequestApprovalAsync(
                Arg.Any<ApprovalContext>(),
                Arg.Do<string>(r => capturedReason = r),
                Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalDecision.Allow("test-user"));

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            _ => Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)),
            CancellationToken.None);

        capturedReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NonToolCommand_PassesThrough()
    {
        var behavior = new ApprovalBehavior<string, ToolResponse<object>>(_discovery, _gate);
        var called   = false;
        var corrId   = Guid.NewGuid();

        await behavior.Handle(
            "not-a-tool-command",
            _ => { called = true; return Task.FromResult(ToolResponse<object>.Ok(corrId, new object())); },
            CancellationToken.None);

        called.Should().BeTrue();
        _discovery.DidNotReceive()
            .Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }
}
