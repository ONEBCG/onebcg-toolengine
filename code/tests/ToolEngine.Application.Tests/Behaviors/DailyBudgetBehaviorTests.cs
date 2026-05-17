namespace ToolEngine.Application.Tests.Behaviors;

using FluentAssertions;
using NSubstitute;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Tests.Helpers;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using Xunit;

public sealed class DailyBudgetBehaviorTests
{
    private readonly IReadRepository<Tenant, string>               _tenantRepo =
        Substitute.For<IReadRepository<Tenant, string>>();
    private readonly IReadRepository<ToolInvocationRecord, Guid>   _invocationRepo =
        Substitute.For<IReadRepository<ToolInvocationRecord, Guid>>();

    private DailyBudgetBehavior<
        ToolEngine.Application.Commands.ExecuteToolCommand<object, object>,
        ToolResponse<object>> CreateBehavior() =>
        new(_tenantRepo, _invocationRepo);

    private void SetupTenant(Tenant? tenant) =>
        _tenantRepo
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tenant));

    private void SetupTodayCount(int count) =>
        _invocationRepo
            .CountAsync(Arg.Any<ISpecification<ToolInvocationRecord>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(count));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZeroBudget_PassesThrough()
    {
        // DailyToolCallBudget == 0 means no cap
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 25_000, dailyBudget: 0);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();
        var called   = false;

        var result = await behavior.Handle(
            cmd,
            _ => { called = true; return Task.FromResult(CommandBuilder.BuildResponse(cmd.CorrelationId)); },
            CancellationToken.None);

        called.Should().BeTrue();
        result.Success.Should().BeTrue();
        await _invocationRepo.DidNotReceive()
            .CountAsync(Arg.Any<ISpecification<ToolInvocationRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NegativeBudget_PassesThrough()
    {
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 25_000, dailyBudget: -1);
        SetupTenant(tenant);

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
    public async Task TodayCountBelowBudget_PassesThrough()
    {
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 25_000, dailyBudget: 100);
        SetupTenant(tenant);
        SetupTodayCount(50);

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
    public async Task TodayCountExactlyAtBudget_Returns429_DAILY_BUDGET_EXCEEDED()
    {
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 25_000, dailyBudget: 100);
        SetupTenant(tenant);
        SetupTodayCount(100); // count >= budget → reject

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("DAILY_BUDGET_EXCEEDED");
        result.Error.HttpStatusCode.Should().Be(429);
    }

    [Fact]
    public async Task TodayCountOverBudget_Returns429()
    {
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 25_000, dailyBudget: 100);
        SetupTenant(tenant);
        SetupTodayCount(150);

        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("DAILY_BUDGET_EXCEEDED");
        result.Error.HttpStatusCode.Should().Be(429);
    }

    [Fact]
    public async Task NullTenant_PassesThrough()
    {
        // TenantAuthorizationBehavior handles rejection; DailyBudget passes through
        SetupTenant(null);

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
    public async Task NonToolCommand_PassesThrough()
    {
        var behavior = new DailyBudgetBehavior<string, ToolResponse<object>>(_tenantRepo, _invocationRepo);
        var called   = false;
        var corrId   = Guid.NewGuid();

        await behavior.Handle(
            "not-a-tool-command",
            _ => { called = true; return Task.FromResult(ToolResponse<object>.Ok(corrId, new object())); },
            CancellationToken.None);

        called.Should().BeTrue();
        await _tenantRepo.DidNotReceive()
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
