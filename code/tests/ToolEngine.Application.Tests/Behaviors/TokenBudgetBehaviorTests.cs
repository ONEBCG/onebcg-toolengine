namespace ToolEngine.Application.Tests.Behaviors;

using FluentAssertions;
using NSubstitute;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Tests.Helpers;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using Xunit;

public sealed class TokenBudgetBehaviorTests
{
    private readonly IReadRepository<Tenant, string> _tenantRepo =
        Substitute.For<IReadRepository<Tenant, string>>();

    private TokenBudgetBehavior<
        ToolEngine.Application.Commands.ExecuteToolCommand<object, object>,
        ToolResponse<object>> CreateBehavior() =>
        new(_tenantRepo);

    private void SetupTenant(Tenant? tenant) =>
        _tenantRepo
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tenant));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZeroCap_PassesThrough()
    {
        // MaxResponseTokens == 0 means no cap
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 0, dailyBudget: 10_000);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithMaxResponseTokens(100_000).Build();
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
    public async Task RequestedTokensAtCap_PassesThrough()
    {
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 25_000, dailyBudget: 10_000);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithMaxResponseTokens(25_000).Build();
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
    public async Task RequestedTokensOverCap_Returns400_TOKEN_BUDGET_EXCEEDED()
    {
        var tenant = TenantBuilder.Active();
        tenant.SetLimits(maxResponseTokens: 10_000, dailyBudget: 10_000);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithMaxResponseTokens(50_000).Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("TOKEN_BUDGET_EXCEEDED");
        result.Error.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task NullTenant_PassesThrough()
    {
        // TenantAuthorizationBehavior will reject; TokenBudget just passes through
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
        var behavior = new TokenBudgetBehavior<string, ToolResponse<object>>(_tenantRepo);
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
