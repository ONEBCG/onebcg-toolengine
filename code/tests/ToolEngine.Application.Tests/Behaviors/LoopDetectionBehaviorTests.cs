namespace ToolEngine.Application.Tests.Behaviors;

using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Tests.Helpers;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Contracts;
using Xunit;

public sealed class LoopDetectionBehaviorTests
{
    private readonly ICacheProvider _cache = Substitute.For<ICacheProvider>();

    private LoopDetectionBehavior<
        ToolEngine.Application.Commands.ExecuteToolCommand<object, object>,
        ToolResponse<object>> CreateBehavior(int maxCalls = 10) =>
        new(_cache, Options.Create(new LoopDetectionOptions { MaxCallsPerCorrelation = maxCalls }));

    private void SetupIncrementReturns(int count) =>
        _cache
            .IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(count));

    private static string ExpectedKey(
        ToolEngine.Application.Commands.ExecuteToolCommand<object, object> cmd) =>
        $"loop:{cmd.CorrelationId}:{cmd.ToolNamespace}.{cmd.ToolName}";

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstCall_PassesThrough()
    {
        SetupIncrementReturns(1);
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
    public async Task AtMax_PassesThrough()
    {
        SetupIncrementReturns(10); // exactly at max — not over
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior(maxCalls: 10);
        var called   = false;

        var result = await behavior.Handle(
            cmd,
            _ => { called = true; return Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)); },
            CancellationToken.None);

        called.Should().BeTrue();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task OverMax_Returns429_AGENT_LOOP_DETECTED()
    {
        SetupIncrementReturns(11); // count > max (10) → circuit open
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior(maxCalls: 10);

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("AGENT_LOOP_DETECTED");
        result.Error.HttpStatusCode.Should().Be(429);
    }

    [Fact]
    public async Task OverMax_RemovesCacheKey()
    {
        SetupIncrementReturns(11);
        var cmd         = new CommandBuilder().Build();
        var behavior    = CreateBehavior(maxCalls: 10);
        var expectedKey = ExpectedKey(cmd);

        await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        await _cache.Received(1)
            .RemoveAsync(expectedKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DifferentTools_SameCorrelation_HaveDifferentKeys()
    {
        var correlationId = Guid.NewGuid();
        var capturedKeys  = new List<string>();

        _cache
            .IncrementAsync(
                Arg.Do<string>(k => capturedKeys.Add(k)),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var cmd1 = new CommandBuilder().WithCorrelationId(correlationId).WithToolName("tool-a").Build();
        var cmd2 = new CommandBuilder().WithCorrelationId(correlationId).WithToolName("tool-b").Build();

        var behavior = CreateBehavior();

        await behavior.Handle(cmd1, FakeDelegates.SuccessResponse(cmd1.CorrelationId), CancellationToken.None);
        await behavior.Handle(cmd2, FakeDelegates.SuccessResponse(cmd2.CorrelationId), CancellationToken.None);

        capturedKeys.Should().HaveCount(2);
        capturedKeys[0].Should().NotBe(capturedKeys[1]);
        capturedKeys[0].Should().Contain("tool-a");
        capturedKeys[1].Should().Contain("tool-b");
    }

    [Fact]
    public async Task ConfigurableMaxCalls_IsRespected()
    {
        SetupIncrementReturns(4); // count > maxCalls(3) → loop detected
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior(maxCalls: 3);

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("AGENT_LOOP_DETECTED");
    }

    [Fact]
    public async Task NonToolCommand_PassesThrough()
    {
        var behavior = new LoopDetectionBehavior<string, ToolResponse<object>>(
            _cache, Options.Create(new LoopDetectionOptions()));

        var called = false;
        var corrId = Guid.NewGuid();

        await behavior.Handle(
            "not-a-tool-command",
            _ => { called = true; return Task.FromResult(ToolResponse<object>.Ok(corrId, new object())); },
            CancellationToken.None);

        called.Should().BeTrue();
        await _cache.DidNotReceive()
            .IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}
