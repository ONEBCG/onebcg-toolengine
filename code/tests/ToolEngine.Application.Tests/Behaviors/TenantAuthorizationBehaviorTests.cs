namespace ToolEngine.Application.Tests.Behaviors;

using FluentAssertions;
using NSubstitute;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Tests.Helpers;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using Xunit;

public sealed class TenantAuthorizationBehaviorTests
{
    private readonly IReadRepository<Tenant, string> _tenantRepo =
        Substitute.For<IReadRepository<Tenant, string>>();

    private TenantAuthorizationBehavior<
        ToolEngine.Application.Commands.ExecuteToolCommand<object, object>,
        ToolResponse<object>> CreateBehavior() =>
        new(_tenantRepo);

    private void SetupTenant(Tenant? tenant) =>
        _tenantRepo
            .GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tenant));

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantNotFound_Returns401_WithUnauthorizedCode()
    {
        SetupTenant(null);
        var cmd      = new CommandBuilder().WithTenantId("missing-tenant").Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("UNAUTHORIZED");
        result.Error.HttpStatusCode.Should().Be(401);
    }

    [Fact]
    public async Task TenantInactive_Returns403_WithUnauthorizedCode()
    {
        var tenant = TenantBuilder.Inactive();
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithTenantId(tenant.Id).Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("UNAUTHORIZED");
        result.Error.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task EmptyAllowlist_DeniesNamespace_Returns403()
    {
        var tenant = TenantBuilder.Active("tenant-empty", allowedNamespaces: []);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithTenantId(tenant.Id).WithToolNamespace("math").Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("UNAUTHORIZED");
        result.Error.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task WildcardAllowlist_AllowsAllNamespaces()
    {
        var tenant = TenantBuilder.Active("tenant-wildcard", allowedNamespaces: ["*"]);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithTenantId(tenant.Id).WithToolNamespace("any-namespace").Build();
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
    public async Task ExactNamespaceMatch_CaseInsensitive_Allows()
    {
        var tenant = TenantBuilder.Active("tenant-exact", allowedNamespaces: ["math"]);
        SetupTenant(tenant);

        // Use uppercase "MATH" — should match case-insensitively
        var cmd      = new CommandBuilder().WithTenantId(tenant.Id).WithToolNamespace("MATH").Build();
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
    public async Task NamespaceNotInAllowlist_Denies_Returns403()
    {
        var tenant = TenantBuilder.Active("tenant-restricted", allowedNamespaces: ["math"]);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithTenantId(tenant.Id).WithToolNamespace("payment").Build();
        var behavior = CreateBehavior();

        var result = await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("UNAUTHORIZED");
        result.Error.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task EmptyToolNamespace_SkipsNamespaceCheck()
    {
        // Tenant with empty allowlist — but namespace is empty, so check is skipped
        var tenant = TenantBuilder.Active("tenant-nons", allowedNamespaces: []);
        SetupTenant(tenant);

        var cmd      = new CommandBuilder().WithTenantId(tenant.Id).WithToolNamespace("").Build();
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
    public async Task NonToolCommand_PassesThrough_WithoutTenantLookup()
    {
        var behavior = new TenantAuthorizationBehavior<string, ToolResponse<object>>(_tenantRepo);
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
