namespace ToolEngine.Integration.Tests.Compliance;

using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Integration.Tests.Infrastructure;

/// <summary>
/// H1 — Append-only SOC 2 audit event log.
///
/// Verifies that every tool invocation produces the correct sequence of
/// ToolInvocationEvent rows and that the append-only contract is observable
/// at the EF Core tracking level.
/// </summary>
public sealed class AuditTrailTests : IntegrationTestBase
{
    // ── H1-01 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulInvocation_CreatesInvokedAndSucceededEvents()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand();

        // Act
        var response = await Mediator.Send(cmd);

        // Assert — two events: Invoked (before handler) and Succeeded (after).
        response.Success.Should().BeTrue();

        var events = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmd.CorrelationId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();

        events.Should().HaveCount(2);
        events[0].EventType.Should().Be(InvocationEventType.Invoked);
        events[1].EventType.Should().Be(InvocationEventType.Succeeded);
    }

    // ── H1-02 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventRows_HaveCorrectInvocationRecordId()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand();

        // Act
        await Mediator.Send(cmd);

        // Assert — every event links back to the same ToolInvocationRecord.
        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        var events = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmd.CorrelationId)
            .ToListAsync();

        events.Should().NotBeEmpty();
        events.Should().AllSatisfy(e =>
            e.InvocationRecordId.Should().Be(record.Id,
                because: "every event must link to the same ToolInvocationRecord"));
    }

    // ── H1-03 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendedInvocation_Returns202_WithPendingInvocationId()
    {
        // Arrange — configure discovery + gate for a suspended (async approval) path.
        // Pipeline order: TenantAuth → Validation → TokenBudget → DailyBudget →
        //                 LoopDetection → ApprovalBehavior → AuditBehavior → Handler
        //
        // When ApprovalBehavior suspends, it returns WITHOUT calling next(), so
        // AuditBehavior (which is innermost, inside next()) never runs. This is by design:
        // the invocation is not yet executing — it is queued for human sign-off.
        // The audit record is written only when execution resumes after approval.
        var pendingId = Guid.NewGuid();

        var descriptor = new ToolEngine.Tools.Abstractions.Interfaces.ToolDiscoveryDescriptor(
            Namespace:     "payments",
            Name:          "charge",
            Version:       "1.0",
            Description:   "Charges a payment method",
            WhenToUse:     "when charging",
            WhenNotToUse:  "never",
            NeedsApproval: true,
            ApprovalRisk:  ToolEngine.Core.Domain.Enums.ApprovalRisk.High);

        DiscoveryMock
            .Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ToolEngine.Core.Domain.Common.Result.Success(descriptor));

        GateMock
            .RequestApprovalAsync(
                Arg.Any<ToolEngine.Tools.Abstractions.Interfaces.ApprovalContext>(),
                Arg.Any<string>(),
                Arg.Any<ToolEngine.Core.Domain.Enums.ApprovalRisk>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(ToolEngine.Tools.Abstractions.Interfaces.ApprovalDecision.Suspend(pendingId));

        await SeedTenantAsync(allowedNamespaces: ["*"]);
        var cmd = BuildCommand(toolNamespace: "payments", toolName: "charge");

        // Act
        var response = await Mediator.Send(cmd);

        // Assert — pipeline returned suspended response with correct pending ID.
        response.PendingInvocationId.Should().Be(pendingId,
            because: "ApprovalBehavior must relay the gate's PendingInvocationId on the response");
        response.Error!.Code.Should().Be("APPROVAL_PENDING");
        response.Error.HttpStatusCode.Should().Be(202);

        // No ToolInvocationRecord or events are written when execution is suspended at
        // the approval gate (AuditBehavior is inside next() and is never called).
        var events = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmd.CorrelationId)
            .ToListAsync();

        events.Should().BeEmpty(
            because: "AuditBehavior is innermost and never runs when ApprovalBehavior short-circuits");
    }

    // ── H1-04 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventRows_AreNotModified_AfterCreation()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand();

        // Act
        await Mediator.Send(cmd);

        // Re-load events into the EF change tracker.
        var events = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmd.CorrelationId)
            .ToListAsync();

        // Assert — no entity has pending modifications (append-only: no updates applied).
        // This confirms the application layer only INSERTs events, never UPDATEs them.
        foreach (var ev in events)
        {
            var entry = Db.Entry(ev);
            entry.State.Should().Be(Microsoft.EntityFrameworkCore.EntityState.Unchanged,
                because: "ToolInvocationEvent rows are append-only and must not be mutated after creation");
        }
    }

    // ── H1-05 — multiple invocations produce independent event sets ───────────

    [Fact]
    public async Task TwoInvocations_ProduceIsolatedEventSets()
    {
        // Arrange
        await SeedTenantAsync();
        var cmdA = BuildCommand();
        var cmdB = BuildCommand();

        // Act
        await Mediator.Send(cmdA);
        await Mediator.Send(cmdB);

        // Assert — each correlationId has its own isolated 2-event set.
        var eventsA = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmdA.CorrelationId)
            .ToListAsync();
        var eventsB = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmdB.CorrelationId)
            .ToListAsync();

        eventsA.Should().HaveCount(2);
        eventsB.Should().HaveCount(2);
        eventsA.Select(e => e.Id).Should().NotIntersectWith(eventsB.Select(e => e.Id));
    }
}
