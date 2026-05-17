namespace ToolEngine.Integration.Tests.Compliance;

using Microsoft.EntityFrameworkCore;
using ToolEngine.Integration.Tests.Infrastructure;

/// <summary>
/// H2 — GDPR retention and anonymisation.
///
/// GDPR Article 17 "right to erasure" is implemented via ToolInvocationRecord.Anonymize().
/// Article 5(1)(e) "storage limitation" is implemented via RetainUntil (90-day window).
///
/// These tests verify that the field values are set correctly by the AuditBehavior
/// and that the anonymisation operation is idempotent and structure-preserving.
/// </summary>
public sealed class GdprRetentionTests : IntegrationTestBase
{
    // ── H2-01 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolInvocationRecord_HasRetainUntil_SetToApprox90Days()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand();

        // Act
        var response = await Mediator.Send(cmd);

        // Assert — RetainUntil is at least 89 days from now (AuditBehavior sets 90 days).
        response.Success.Should().BeTrue();

        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.RetainUntil.Should().BeAfter(
            DateTimeOffset.UtcNow.AddDays(89),
            because: "GDPR retention window must be at least 90 days from invocation time");
    }

    // ── H2-02 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymize_NullsUserId_And_SetsAnonymizedFlag()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand();
        await Mediator.Send(cmd);

        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.UserId.Should().NotBe("[anonymized]", because: "UserId is populated before anonymisation");

        // Act
        record.Anonymize();
        await Db.SaveChangesAsync();

        // Re-load to verify persisted state.
        Db.ChangeTracker.Clear();
        var anonymized = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        // Assert
        anonymized.UserId.Should().Be("[anonymized]");
        anonymized.IsAnonymized.Should().BeTrue();
    }

    // ── H2-03 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymize_IsIdempotent_DoesNotThrow()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand();
        await Mediator.Send(cmd);

        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        // Act — call Anonymize twice; second call must be a no-op, not an exception.
        var act = () =>
        {
            record.Anonymize();
            record.Anonymize();
        };

        // Assert
        act.Should().NotThrow(because: "Anonymize is documented as idempotent");
        record.IsAnonymized.Should().BeTrue();
        record.UserId.Should().Be("[anonymized]");
    }

    // ── H2-04 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymize_PreservesStructuralFields()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand(toolNamespace: "math", toolName: "calculate");
        await Mediator.Send(cmd);

        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        var toolNameBefore   = record.ToolName;
        var tenantIdBefore   = record.TenantId;
        var statusBefore     = record.Status;
        var invokedAtBefore  = record.InvokedAt;

        // Act
        record.Anonymize();

        // Assert — structural fields are unchanged; only PII fields are nulled.
        record.ToolName.Should().Be(toolNameBefore,
            because: "ToolName is structural and must survive anonymisation");
        record.TenantId.Should().Be(tenantIdBefore,
            because: "TenantId is structural and must survive anonymisation");
        record.Status.Should().Be(statusBefore,
            because: "Status is structural and must survive anonymisation");
        record.InvokedAt.Should().Be(invokedAtBefore,
            because: "InvokedAt is structural and must survive anonymisation");
    }

    // ── H2-05 — GovernanceMetadata is cleared by Anonymize ───────────────────

    [Fact]
    public async Task Anonymize_ClearsGovernanceMetadataJson()
    {
        // Arrange — command carries governance metadata which may contain PII.
        await SeedTenantAsync();
        var cmd = BuildCommand(governanceMetadata: "{\"operator\":\"alice@example.com\"}");
        await Mediator.Send(cmd);

        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.GovernanceMetadataJson.Should().NotBeNull(
            because: "GovernanceMetadataJson was provided in the command");

        // Act
        record.Anonymize();

        // Assert
        record.GovernanceMetadataJson.Should().BeNull(
            because: "governance metadata may contain PII and must be cleared on anonymisation");
    }
}
