namespace ToolEngine.Domain.Tests.Contracts;

using System.Text.Json;
using FluentAssertions;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using Xunit;

public sealed class AcknowledgementStatementTests
{
    private static AcknowledgementStatement BuildStatement() =>
        new AcknowledgementStatement(
            RegulatoryBasis:   "EU AI Act Article 14 §4",
            RiskLevel:         ApprovalRisk.High,
            ToolFullName:      "payments.charge-card",
            OperatorStatement: "I confirm I have reviewed the proposed action and accept responsibility.",
            IssuedAt:          new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Record_SerializesAndDeserializes_RoundTrip()
    {
        var original = BuildStatement();

        var json       = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AcknowledgementStatement>(json);

        deserialized.Should().NotBeNull();
        deserialized!.RegulatoryBasis.Should().Be(original.RegulatoryBasis);
        deserialized.RiskLevel.Should().Be(original.RiskLevel);
        deserialized.ToolFullName.Should().Be(original.ToolFullName);
        deserialized.OperatorStatement.Should().Be(original.OperatorStatement);
        deserialized.IssuedAt.Should().Be(original.IssuedAt);
    }

    [Fact]
    public void Record_ContainsRegulatoryBasis()
    {
        var statement = BuildStatement();

        statement.RegulatoryBasis.Should().Be("EU AI Act Article 14 §4");
    }

    [Fact]
    public void Record_ContainsRiskLevel()
    {
        var statement = BuildStatement();

        statement.RiskLevel.Should().Be(ApprovalRisk.High);
    }
}
