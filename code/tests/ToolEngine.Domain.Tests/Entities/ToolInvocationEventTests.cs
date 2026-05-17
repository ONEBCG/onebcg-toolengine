namespace ToolEngine.Domain.Tests.Entities;

using FluentAssertions;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using Xunit;

public sealed class ToolInvocationEventTests
{
    private static ToolInvocationEvent BuildEvent(
        CallerType          callerType             = CallerType.Human,
        string?             governanceMetadataJson = null,
        double?             durationMs             = null,
        InvocationEventType eventType              = InvocationEventType.Invoked)
    {
        return ToolInvocationEvent.Create(
            invocationRecordId:    Guid.NewGuid(),
            correlationId:         Guid.NewGuid(),
            tenantId:              "tenant-1",
            userId:                "user-abc",
            callerType:            callerType,
            toolNamespace:         "payments",
            toolName:              "charge-card",
            toolVersion:           "1.0.0",
            eventType:             eventType,
            durationMs:            durationMs,
            governanceMetadataJson: governanceMetadataJson);
    }

    [Fact]
    public void Create_SetsCallerType_FromParameter()
    {
        var evt = BuildEvent(callerType: CallerType.AiAgent);

        evt.CallerType.Should().Be(CallerType.AiAgent);
    }

    [Fact]
    public void Create_SetsGovernanceMetadataJson()
    {
        const string json = """{"policy":"iso42001","version":"1.0"}""";
        var evt = BuildEvent(governanceMetadataJson: json);

        evt.GovernanceMetadataJson.Should().Be(json);
    }

    [Fact]
    public void Create_DurationMs_IsNull_WhenNotProvided()
    {
        var evt = BuildEvent(durationMs: null);

        evt.DurationMs.Should().BeNull();
    }

    [Fact]
    public void Create_DurationMs_IsSet_WhenProvided()
    {
        var evt = BuildEvent(durationMs: 123.45);

        evt.DurationMs.Should().Be(123.45);
    }

    [Fact]
    public void Create_SetsEventType()
    {
        var evt = BuildEvent(eventType: InvocationEventType.Succeeded);

        evt.EventType.Should().Be(InvocationEventType.Succeeded);
    }

    [Fact]
    public void ToolFullName_ComputedCorrectly()
    {
        var evt = BuildEvent();

        evt.ToolFullName.Should().Be("payments.charge-card");
    }
}
