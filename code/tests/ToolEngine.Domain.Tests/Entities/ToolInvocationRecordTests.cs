namespace ToolEngine.Domain.Tests.Entities;

using FluentAssertions;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using Xunit;

internal sealed class FakeClock : IDateTimeProvider
{
    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;
    public DateTimeOffset UtcNow { get; }
}

public sealed class ToolInvocationRecordTests
{
    private static readonly DateTimeOffset FixedNow =
        new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);

    private static ToolInvocationRecord BuildRecord(
        CallerType callerType             = CallerType.Human,
        string?    governanceMetadataJson = null,
        int        retentionDays          = 90)
    {
        var clock = new FakeClock(FixedNow);
        return ToolInvocationRecord.Create(
            correlationId:         Guid.NewGuid(),
            tenantId:              "tenant-1",
            userId:                "user-abc",
            toolNamespace:         "payments",
            toolName:              "charge-card",
            toolVersion:           "1.0.0",
            toolType:              ToolType.Api,
            clock:                 clock,
            callerType:            callerType,
            governanceMetadataJson: governanceMetadataJson,
            retentionDays:         retentionDays);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsRetainUntil_NinetyDaysFromInvokedAt()
    {
        var record = BuildRecord();

        record.RetainUntil.Should().Be(FixedNow.AddDays(90));
    }

    [Fact]
    public void Create_SetsCallerType_AiAgent()
    {
        var record = BuildRecord(callerType: CallerType.AiAgent);

        record.CallerType.Should().Be(CallerType.AiAgent);
    }

    [Fact]
    public void Create_SetsGovernanceMetadataJson()
    {
        const string json = """{"policy":"iso42001","version":"1.0"}""";
        var record = BuildRecord(governanceMetadataJson: json);

        record.GovernanceMetadataJson.Should().Be(json);
    }

    [Fact]
    public void Create_DefaultCallerType_IsHuman()
    {
        var clock  = new FakeClock(FixedNow);
        var record = ToolInvocationRecord.Create(
            correlationId: Guid.NewGuid(),
            tenantId:      "tenant-1",
            userId:        "user-abc",
            toolNamespace: "payments",
            toolName:      "charge-card",
            toolVersion:   "1.0.0",
            toolType:      ToolType.Api,
            clock:         clock);

        record.CallerType.Should().Be(CallerType.Human);
    }

    // ── Anonymize ─────────────────────────────────────────────────────────────

    [Fact]
    public void Anonymize_SetsIsAnonymized()
    {
        var record = BuildRecord();
        record.Anonymize();

        record.IsAnonymized.Should().BeTrue();
    }

    [Fact]
    public void Anonymize_NullsUserId_WithPlaceholder()
    {
        var record = BuildRecord();
        record.Anonymize();

        record.UserId.Should().Be("[anonymized]");
    }

    [Fact]
    public void Anonymize_NullsErrorMessage()
    {
        var clock  = new FakeClock(FixedNow);
        var record = BuildRecord();
        record.MarkFailed(new ToolError("ERR", "Sensitive error detail"), clock);

        record.Anonymize();

        record.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Anonymize_NullsGovernanceMetadataJson()
    {
        var record = BuildRecord(governanceMetadataJson: """{"policy":"iso42001"}""");
        record.Anonymize();

        record.GovernanceMetadataJson.Should().BeNull();
    }

    [Fact]
    public void Anonymize_IsIdempotent()
    {
        var record = BuildRecord(governanceMetadataJson: """{"policy":"iso42001"}""");
        record.Anonymize();
        record.Anonymize(); // second call — must not throw or reset state

        record.IsAnonymized.Should().BeTrue();
        record.UserId.Should().Be("[anonymized]");
    }

    // ── MarkSucceeded ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkSucceeded_SetsStatusAndMetrics()
    {
        var clock   = new FakeClock(FixedNow);
        var record  = BuildRecord();
        var metrics = new ToolUsageMetrics(
            Duration:   TimeSpan.FromMilliseconds(250),
            RetryCount: 1,
            TokensIn:   100,
            TokensOut:  200);

        record.MarkSucceeded(metrics, clock);

        record.Status.Should().Be(ToolStatus.Succeeded);
        record.CompletedAt.Should().Be(FixedNow);
        record.Duration.Should().Be(TimeSpan.FromMilliseconds(250));
        record.TokensIn.Should().Be(100);
        record.TokensOut.Should().Be(200);
        record.RetryCount.Should().Be(1);
    }

    // ── MarkFailed ────────────────────────────────────────────────────────────

    [Fact]
    public void MarkFailed_SetsErrorCodeAndMessage()
    {
        var clock  = new FakeClock(FixedNow);
        var record = BuildRecord();
        var error  = new ToolError("GATEWAY_TIMEOUT", "Upstream service did not respond", 504);

        record.MarkFailed(error, clock);

        record.Status.Should().Be(ToolStatus.Failed);
        record.ErrorCode.Should().Be("GATEWAY_TIMEOUT");
        record.ErrorMessage.Should().Be("Upstream service did not respond");
    }

    // ── ToolFullName ──────────────────────────────────────────────────────────

    [Fact]
    public void ToolFullName_IncludesNamespace_WhenProvided()
    {
        var clock  = new FakeClock(FixedNow);
        var record = ToolInvocationRecord.Create(
            correlationId: Guid.NewGuid(),
            tenantId:      "tenant-1",
            userId:        "user-abc",
            toolNamespace: "payments",
            toolName:      "charge-card",
            toolVersion:   "1.0.0",
            toolType:      ToolType.Api,
            clock:         clock);

        record.ToolFullName.Should().Be("payments.charge-card");
    }

    [Fact]
    public void ToolFullName_IsJustName_WhenNamespaceEmpty()
    {
        var clock  = new FakeClock(FixedNow);
        var record = ToolInvocationRecord.Create(
            correlationId: Guid.NewGuid(),
            tenantId:      "tenant-1",
            userId:        "user-abc",
            toolNamespace: "",
            toolName:      "charge-card",
            toolVersion:   "1.0.0",
            toolType:      ToolType.Api,
            clock:         clock);

        record.ToolFullName.Should().Be("charge-card");
    }
}
