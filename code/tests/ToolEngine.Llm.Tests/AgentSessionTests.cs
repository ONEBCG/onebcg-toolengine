namespace ToolEngine.Llm.Tests;

using FluentAssertions;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Session;
using Xunit;

public sealed class AgentSessionTests
{
    [Fact]
    public void NewSession_HasZeroTokens()
    {
        var session = new AgentSession();
        session.TokensUsed.Should().Be(0);
    }

    [Fact]
    public void AddMessage_IncrementsMessageCount()
    {
        var session = new AgentSession();
        session.AddMessage(LlmMessage.User("Hello"));
        session.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void RecordUsage_AccumulatesAcrossMultipleCalls()
    {
        var session = new AgentSession();
        session.RecordUsage(new LlmUsage(100, 50));
        session.RecordUsage(new LlmUsage(200, 75));

        session.TotalUsage.InputTokens.Should().Be(300);
        session.TotalUsage.OutputTokens.Should().Be(125);
        session.TotalUsage.TotalTokens.Should().Be(425);
    }

    [Fact]
    public void RecordUsage_AccumulatesCost()
    {
        var session = new AgentSession();
        session.RecordUsage(new LlmUsage(100, 50, 0.05m));
        session.RecordUsage(new LlmUsage(100, 50, 0.03m));

        session.TotalUsage.EstimatedCostUsd.Should().Be(0.08m);
    }

    [Fact]
    public void IsSingleTurn_DefaultsToFalse()
    {
        var session = new AgentSession();
        session.IsSingleTurn.Should().BeFalse();
    }

    [Fact]
    public void SessionId_IsAssigned_OnCreation()
    {
        var session = new AgentSession { SessionId = "test-id" };
        session.SessionId.Should().Be("test-id");
    }

    [Fact]
    public void Messages_AreReturnedInOrder()
    {
        var session = new AgentSession();
        session.AddMessage(LlmMessage.User("First"));
        session.AddMessage(LlmMessage.Assistant("Second"));

        session.Messages[0].Content.Should().Be("First");
        session.Messages[1].Content.Should().Be("Second");
    }
}
