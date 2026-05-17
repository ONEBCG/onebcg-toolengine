namespace ToolEngine.Domain.Tests.Entities;

using System.Text.RegularExpressions;
using FluentAssertions;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using Xunit;

public sealed class PendingApprovalTests
{
    private static PendingApproval BuildApproval(
        string? idempotencyKey = null,
        int     timeoutMinutes = 60)
    {
        return PendingApproval.Create(
            correlationId:  Guid.NewGuid(),
            tenantId:       "tenant-1",
            userId:         "user-abc",
            toolNamespace:  "payments",
            toolName:       "charge-card",
            toolVersion:    "1.0.0",
            serializedInput: """{"amount":100}""",
            channel:        ApprovalChannelType.Dashboard,
            risk:           ApprovalRisk.High,
            approvalReason: "Charge exceeds daily limit.",
            approverEmail:  "approver@example.com",
            timeoutMinutes: timeoutMinutes,
            idempotencyKey: idempotencyKey);
    }

    // ── ApprovalToken ─────────────────────────────────────────────────────────

    [Fact]
    public void Create_GeneratesApprovalToken_64HexChars()
    {
        var approval = BuildApproval();

        approval.ApprovalToken.Should().HaveLength(64);
    }

    [Fact]
    public void Create_ApprovalToken_IsValidHex()
    {
        var approval = BuildApproval();

        Regex.IsMatch(approval.ApprovalToken, "^[0-9A-F]{64}$")
             .Should().BeTrue(because: "token must be uppercase hex from Convert.ToHexString");
    }

    [Fact]
    public void Create_ApprovalTokens_AreUnique()
    {
        var a = BuildApproval();
        var b = BuildApproval();

        a.ApprovalToken.Should().NotBe(b.ApprovalToken);
    }

    // ── IdempotencyKey ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsIdempotencyKey()
    {
        var approval = BuildApproval(idempotencyKey: "idem-key-123");

        approval.IdempotencyKey.Should().Be("idem-key-123");
    }

    // ── SetAcknowledgement ────────────────────────────────────────────────────

    [Fact]
    public void SetAcknowledgement_StoresJson()
    {
        var approval = BuildApproval();
        const string json = """{"regulatoryBasis":"EU AI Act Article 14"}""";

        approval.SetAcknowledgement(json);

        approval.AcknowledgementJson.Should().Be(json);
    }

    [Fact]
    public void SetAcknowledgement_IsIdempotent_SecondCallIgnored()
    {
        var approval = BuildApproval();
        const string firstJson  = """{"regulatoryBasis":"EU AI Act Article 14"}""";
        const string secondJson = """{"regulatoryBasis":"SOMETHING ELSE"}""";

        approval.SetAcknowledgement(firstJson);
        approval.SetAcknowledgement(secondJson); // must be ignored

        approval.AcknowledgementJson.Should().Be(firstJson);
    }

    // ── IncrementFailedOtpAttempts ────────────────────────────────────────────

    [Fact]
    public void IncrementFailedOtpAttempts_ReturnsFalse_BelowMax()
    {
        var approval = BuildApproval();

        var result1 = approval.IncrementFailedOtpAttempts(maxAttempts: 5);
        var result2 = approval.IncrementFailedOtpAttempts(maxAttempts: 5);
        var result3 = approval.IncrementFailedOtpAttempts(maxAttempts: 5);
        var result4 = approval.IncrementFailedOtpAttempts(maxAttempts: 5);

        result1.Should().BeFalse();
        result2.Should().BeFalse();
        result3.Should().BeFalse();
        result4.Should().BeFalse();
    }

    [Fact]
    public void IncrementFailedOtpAttempts_ReturnsTrue_AtMaxAttempts()
    {
        var approval = BuildApproval();

        bool result = false;
        for (var i = 0; i < 5; i++)
            result = approval.IncrementFailedOtpAttempts(maxAttempts: 5);

        result.Should().BeTrue();
    }

    [Fact]
    public void IncrementFailedOtpAttempts_ExpiresApproval_AtMax()
    {
        var approval = BuildApproval();

        for (var i = 0; i < 5; i++)
            approval.IncrementFailedOtpAttempts(maxAttempts: 5);

        approval.Status.Should().Be(ApprovalStatus.Expired);
    }

    // ── Approve / Deny on non-Pending ─────────────────────────────────────────

    [Fact]
    public void Approve_Fails_WhenStatusIsApproved()
    {
        var approval = BuildApproval();
        approval.Approve("approver-1");

        var result = approval.Approve("approver-1");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Approve_Fails_WhenStatusIsDenied()
    {
        var approval = BuildApproval();
        approval.Deny("approver-1");

        var result = approval.Approve("approver-1");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Deny_Fails_WhenStatusIsDenied()
    {
        var approval = BuildApproval();
        approval.Deny("approver-1");

        var result = approval.Deny("approver-1");

        result.IsFailure.Should().BeTrue();
    }
}
