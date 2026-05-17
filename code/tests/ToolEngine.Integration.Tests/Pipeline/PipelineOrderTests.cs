namespace ToolEngine.Integration.Tests.Pipeline;

using ToolEngine.Integration.Tests.Infrastructure;

/// <summary>
/// Verifies that security-critical pipeline behaviors fire in the correct order.
///
/// Order (outermost → innermost):
///   TenantAuthorizationBehavior → ValidationBehavior → TokenBudgetBehavior
///   → DailyBudgetBehavior → LoopDetectionBehavior → ApprovalBehavior → AuditBehavior
///
/// The tests below assert observable HTTP status codes and error codes to confirm
/// that authorization precedes all other checks (OWASP A01:2025 / deny-by-default F6).
/// </summary>
public sealed class PipelineOrderTests : IntegrationTestBase
{
    // ── H-Auth-01 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownTenant_Returns401_BeforeValidationCanFire()
    {
        // Arrange — no tenant seeded; tenant "ghost" does not exist.
        var cmd = BuildCommand(tenantId: "ghost", toolNamespace: "math", toolName: "calculate");

        // Act
        var response = await Mediator.Send(cmd);

        // Assert — TenantAuthorizationBehavior fires first and returns 401.
        // If ValidationBehavior ran first it would also fail but with a different code.
        response.Success.Should().BeFalse();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("UNAUTHORIZED");
        response.Error.HttpStatusCode.Should().Be(401);
    }

    // ── H-Auth-02 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InactiveTenant_Returns403()
    {
        // Arrange
        var clock  = Services.GetRequiredService<ToolEngine.Core.Abstractions.Common.IDateTimeProvider>();
        var tenant = await SeedTenantAsync("inactive-tenant");
        tenant.Deactivate("admin", clock);

        var repo = Services.GetRequiredService<ToolEngine.Core.Abstractions.Persistence.IRepository<ToolEngine.Core.Domain.Entities.Tenant, string>>();
        var uow  = Services.GetRequiredService<ToolEngine.Core.Abstractions.Persistence.IUnitOfWork>();
        repo.Update(tenant);
        await uow.SaveChangesAsync();

        var cmd = BuildCommand(tenantId: "inactive-tenant");

        // Act
        var response = await Mediator.Send(cmd);

        // Assert
        response.Success.Should().BeFalse();
        response.Error!.Code.Should().Be("UNAUTHORIZED");
        response.Error.HttpStatusCode.Should().Be(403);
    }

    // ── F6 deny-by-default ────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyNamespaceAllowlist_DeniesRequest()
    {
        // Arrange — tenant seeded with empty namespace list (deny-by-default).
        await SeedTenantAsync("restricted-tenant", allowedNamespaces: []);
        var cmd = BuildCommand(tenantId: "restricted-tenant", toolNamespace: "payments");

        // Act
        var response = await Mediator.Send(cmd);

        // Assert — namespace not permitted → 403.
        response.Success.Should().BeFalse();
        response.Error!.Code.Should().Be("UNAUTHORIZED");
        response.Error.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task WildcardNamespaceAllowlist_AllowsAnyNamespace()
    {
        // Arrange — tenant seeded with wildcard → all namespaces allowed.
        await SeedTenantAsync("wildcard-tenant", allowedNamespaces: ["*"]);
        var cmd = BuildCommand(tenantId: "wildcard-tenant", toolNamespace: "any.namespace.at.all");

        // Act
        var response = await Mediator.Send(cmd);

        // Assert — pipeline passes through to StubToolHandler.
        response.Success.Should().BeTrue();
    }
}
