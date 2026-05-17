namespace ToolEngine.Integration.Tests.Compliance;

using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Integration.Tests.Infrastructure;

/// <summary>
/// H4, H5 — Agent identity and ISO 42001 governance metadata persistence.
///
/// H4 (EU AI Act Article 14): CallerType (Human / AiAgent / SystemService) must be
/// persisted on every ToolInvocationRecord and ToolInvocationEvent to enable
/// meaningful human oversight of AI-driven actions.
///
/// H5 (ISO 42001): GovernanceMetadataJson from the X-Governance-Metadata request
/// header must be propagated verbatim to both the record and every audit event.
/// </summary>
public sealed class AgentIdentityTests : IntegrationTestBase
{
    // ── H4-01 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AiAgentCallerType_PersistedOnRecord()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand(callerType: CallerType.AiAgent);

        // Act
        var response = await Mediator.Send(cmd);

        // Assert
        response.Success.Should().BeTrue();

        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.CallerType.Should().Be(CallerType.AiAgent,
            because: "H4: AiAgent identity must be persisted on the audit record");
    }

    // ── H4-02 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HumanCallerType_PersistedOnRecord()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand(callerType: CallerType.Human);

        // Act
        await Mediator.Send(cmd);

        // Assert
        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.CallerType.Should().Be(CallerType.Human,
            because: "H4: Human identity must be persisted on the audit record");
    }

    // ── H4-03 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SystemServiceCallerType_PersistedOnRecord()
    {
        // Arrange
        await SeedTenantAsync();
        var cmd = BuildCommand(callerType: CallerType.SystemService);

        // Act
        await Mediator.Send(cmd);

        // Assert
        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.CallerType.Should().Be(CallerType.SystemService);
    }

    // ── H4-04 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CallerType_PersistedOnAllAuditEvents()
    {
        // Arrange — use AiAgent to make the assertion distinguishable from default.
        await SeedTenantAsync();
        var cmd = BuildCommand(callerType: CallerType.AiAgent);

        // Act
        await Mediator.Send(cmd);

        // Assert — every event for this invocation carries the AiAgent caller type.
        var events = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmd.CorrelationId)
            .ToListAsync();

        events.Should().NotBeEmpty();
        events.Should().AllSatisfy(e =>
            e.CallerType.Should().Be(CallerType.AiAgent,
                because: "H4: CallerType must be propagated to every audit event"));
    }

    // ── H5-01 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GovernanceMetadataJson_PersistedOnRecord()
    {
        // Arrange
        await SeedTenantAsync();
        const string metadata = "{\"model\":\"claude-sonnet\",\"policy\":\"iso42001\"}";
        var cmd = BuildCommand(governanceMetadata: metadata);

        // Act
        await Mediator.Send(cmd);

        // Assert — verbatim JSON is stored on the record.
        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.GovernanceMetadataJson.Should().Be(metadata,
            because: "H5: ISO 42001 governance metadata must be persisted verbatim on the record");
    }

    // ── H5-02 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GovernanceMetadataJson_PersistedOnAllAuditEvents()
    {
        // Arrange
        await SeedTenantAsync();
        const string metadata = "{\"model\":\"claude-haiku\"}";
        var cmd = BuildCommand(governanceMetadata: metadata);

        // Act
        await Mediator.Send(cmd);

        // Assert — every event carries the same governance metadata.
        var events = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmd.CorrelationId)
            .ToListAsync();

        events.Should().NotBeEmpty();
        events.Should().AllSatisfy(e =>
            e.GovernanceMetadataJson.Should().Be(metadata,
                because: "H5: governance metadata must be propagated to every audit event"));
    }

    // ── H5-03 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NullGovernanceMetadata_DoesNotCauseError()
    {
        // Arrange — caller does not supply governance metadata (optional field).
        await SeedTenantAsync();
        var cmd = BuildCommand(governanceMetadata: null);

        // Act
        var response = await Mediator.Send(cmd);

        // Assert — null metadata is valid; pipeline completes successfully.
        response.Success.Should().BeTrue(
            because: "GovernanceMetadataJson is optional and null must not cause a pipeline error");

        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.GovernanceMetadataJson.Should().BeNull();
    }

    // ── H4 + H5 combined ─────────────────────────────────────────────────────

    [Fact]
    public async Task AiAgent_WithGovernanceMetadata_BothFieldsPersistedTogether()
    {
        // Arrange
        await SeedTenantAsync();
        const string metadata = "{\"model\":\"claude-opus\",\"operator\":\"system\"}";
        var cmd = BuildCommand(callerType: CallerType.AiAgent, governanceMetadata: metadata);

        // Act
        await Mediator.Send(cmd);

        // Assert — record carries both H4 and H5 fields.
        var record = await Db.ToolInvocationRecords
            .FirstAsync(r => r.CorrelationId == cmd.CorrelationId);

        record.CallerType.Should().Be(CallerType.AiAgent);
        record.GovernanceMetadataJson.Should().Be(metadata);

        // Assert — events carry both fields.
        var events = await Db.ToolInvocationEvents
            .Where(e => e.CorrelationId == cmd.CorrelationId)
            .ToListAsync();

        events.Should().AllSatisfy(e =>
        {
            e.CallerType.Should().Be(CallerType.AiAgent);
            e.GovernanceMetadataJson.Should().Be(metadata);
        });
    }
}
