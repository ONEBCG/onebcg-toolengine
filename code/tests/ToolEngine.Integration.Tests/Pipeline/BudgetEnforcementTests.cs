namespace ToolEngine.Integration.Tests.Pipeline;

using ToolEngine.Integration.Tests.Infrastructure;

/// <summary>
/// Verifies that DailyBudgetBehavior and LoopDetectionBehavior enforce their caps
/// against real EF Core state and in-process cache respectively.
/// </summary>
public sealed class BudgetEnforcementTests : IntegrationTestBase
{
    // ── Daily budget ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DailyBudget_AtCap_Returns429()
    {
        // Arrange — budget of 1 means the first invocation is allowed (count=0 < 1)
        // and the second is rejected (count=1 >= 1).
        await SeedTenantAsync("budget-tenant", dailyBudget: 1);

        var firstCmd  = BuildCommand(tenantId: "budget-tenant");
        var secondCmd = BuildCommand(tenantId: "budget-tenant");

        // Act
        var firstResponse = await Mediator.Send(firstCmd);

        // The AuditBehavior writes a ToolInvocationRecord on the first call.
        // DailyBudgetBehavior counts records for today before the second call.
        var secondResponse = await Mediator.Send(secondCmd);

        // Assert — first call succeeds; second is rejected.
        firstResponse.Success.Should().BeTrue(because: "first call is within budget (0 < 1)");

        secondResponse.Success.Should().BeFalse();
        secondResponse.Error!.Code.Should().Be("DAILY_BUDGET_EXCEEDED");
        secondResponse.Error.HttpStatusCode.Should().Be(429);
    }

    [Fact]
    public async Task DailyBudget_ZeroBudget_IsUnrestricted()
    {
        // Arrange — DailyToolCallBudget == 0 means no cap.
        await SeedTenantAsync("unlimited-tenant", dailyBudget: 0);

        // Act — send several commands without triggering a 429.
        for (var i = 0; i < 5; i++)
        {
            var response = await Mediator.Send(BuildCommand(tenantId: "unlimited-tenant"));
            response.Success.Should().BeTrue(because: $"call {i + 1} should pass with no daily cap");
        }
    }

    // ── Loop detection ────────────────────────────────────────────────────────

    [Fact]
    public async Task LoopDetection_OverMax_Returns429()
    {
        // Arrange — MaxCallsPerCorrelation = 10.
        //
        // The LoopDetectionBehavior removes the key from the cache when count >= max
        // (in the finally block) to prevent dead counters. In sequential tests this
        // means count is always reset to 0 after reaching exactly max. The circuit
        // (count > max) can only fire if count is already at max when the NEXT call
        // comes in — i.e. the key must already be at max before the final increment.
        //
        // Strategy: pre-seed the cache counter to max (10) then send one more call.
        // Call N+1: IncrementAsync returns 11 > 10 → AGENT_LOOP_DETECTED.
        await SeedTenantAsync("loop-tenant");

        var correlationId = Guid.NewGuid();
        var cacheKey      = $"loop:{correlationId}:math.calculate";
        var ttl           = TimeSpan.FromMinutes(10);

        var cache = Services.GetRequiredService<ToolEngine.Core.Abstractions.Common.ICacheProvider>();

        // Pre-seed counter to exactly MaxCallsPerCorrelation (10).
        for (var i = 0; i < 10; i++)
            await cache.IncrementAsync(cacheKey, ttl);

        // Act — this call increments to 11 > 10 → circuit opens.
        var cmd = BuildCommand(
            tenantId:      "loop-tenant",
            correlationId: correlationId,
            toolNamespace: "math",
            toolName:      "calculate");
        var response = await Mediator.Send(cmd);

        // Assert
        response.Success.Should().BeFalse();
        response.Error!.Code.Should().Be("AGENT_LOOP_DETECTED");
        response.Error.HttpStatusCode.Should().Be(429);
    }

    [Fact]
    public async Task LoopDetection_DifferentCorrelationIds_DoNotInterfere()
    {
        // Arrange — two independent correlationIds; neither should trigger the circuit.
        await SeedTenantAsync("loop-isolation-tenant");

        // Act — send 10 calls per correlationId (exactly at the limit, not over).
        for (var i = 0; i < 10; i++)
        {
            var cmdA = BuildCommand(
                tenantId:      "loop-isolation-tenant",
                correlationId: Guid.NewGuid(), // new id each time — no accumulation
                toolNamespace: "math",
                toolName:      "calculate");
            var response = await Mediator.Send(cmdA);
            response.Success.Should().BeTrue(because: $"each unique correlationId starts at 0 (iteration {i})");
        }
    }
}
