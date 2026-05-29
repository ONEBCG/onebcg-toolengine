using System.Security.Cryptography;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Core.Domain.Entities;

public sealed class PendingApproval : Entity<Guid>
{
    public string          ToolFullName         { get; private set; } = default!;

    // E1 — 256-bit CSPRNG token (not Guid.NewGuid — 122 bit, predictable structure)
    public string          ApprovalToken        { get; private set; } = default!;

    public string?         OtpHash              { get; private set; }   // PBKDF2-HMAC-SHA256 (E3)
    public ApprovalStatus  Status               { get; private set; }
    public ApprovalRisk    Risk                 { get; private set; }
    public ApprovalChannel Channel              { get; private set; }
    public DateTimeOffset  ExpiresAt            { get; private set; }
    public int             FailedOtpAttempts    { get; private set; }   // E2
    public string?         IdempotencyKey       { get; private set; }   // F8
    public string?         AcknowledgementJson  { get; private set; }   // H3
    public string?         SerializedResult     { get; private set; }   // written after approved execution
    public string?         DenialReason         { get; private set; }

    private PendingApproval() { }

    public static PendingApproval Create(
        string toolFullName,
        ApprovalRisk risk, ApprovalChannel channel,
        string? idempotencyKey, IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        return new PendingApproval
        {
            Id             = Guid.NewGuid(),
            ToolFullName   = toolFullName,
            // E1: OWASP minimum 128-bit; we use 256-bit (2×) — 64-char hex string
            ApprovalToken  = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Status         = ApprovalStatus.Pending,
            Risk           = risk,
            Channel        = channel,
            ExpiresAt      = now.AddMinutes(60),
            IdempotencyKey = idempotencyKey,
            CreatedAt      = now,
            UpdatedAt      = now,
        };
    }

    // E2: per-token OTP lockout; returns true when approval should be expired
    public bool IncrementFailedOtpAttempts(int maxAttempts = 5)
    {
        FailedOtpAttempts++;
        UpdatedAt = DateTimeOffset.UtcNow;
        if (FailedOtpAttempts >= maxAttempts)
        {
            Status = ApprovalStatus.Expired;
            return true;
        }
        return false;
    }

    public void SetOtpHash(string hash) { OtpHash = hash; UpdatedAt = DateTimeOffset.UtcNow; }

    // H3 — EU AI Act Article 14 evidence: immutable once set (??=)
    public void SetAcknowledgement(string json)
    {
        AcknowledgementJson ??= json;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(string? serializedResult = null)
    {
        Status           = ApprovalStatus.Approved;
        SerializedResult = serializedResult;
        UpdatedAt        = DateTimeOffset.UtcNow;
    }

    public void Deny(string reason)
    {
        Status       = ApprovalStatus.Denied;
        DenialReason = reason;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        Status    = ApprovalStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
