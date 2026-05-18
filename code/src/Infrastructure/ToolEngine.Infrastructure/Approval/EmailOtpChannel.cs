namespace ToolEngine.Infrastructure.Approval;

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Sends a 6-digit OTP to the approver's email.
/// The approver submits it via POST /approvals/otp.
/// Forced for Critical risk regardless of tenant channel preference.
///
/// OTP is stored as SHA-256 hash — never in plaintext.
/// Production: swap IEmailSender stub with real email provider.
/// </summary>
public sealed class EmailOtpChannel : IApprovalChannel
{
    private readonly IEmailSender    _email;
    private readonly ApprovalOptions _options;
    private readonly ILogger<EmailOtpChannel> _log;

    public ApprovalChannelType ChannelType => ApprovalChannelType.EmailOtp;

    public EmailOtpChannel(
        IEmailSender              email,
        IOptions<ApprovalOptions> options,
        ILogger<EmailOtpChannel>  log)
    {
        _email   = email;
        _options = options.Value;
        _log     = log;
    }

    public async Task SendAsync(PendingApproval approval, CancellationToken ct = default)
    {
        var to = approval.ApproverEmail
                 ?? throw new InvalidOperationException(
                     $"PendingApproval {approval.Id} has no ApproverEmail for EmailOtp channel.");

        var otp     = GenerateOtp();
        var otpHash = HashOtp(otp);
        approval.SetOtpHash(otpHash);

        var subject = $"[ToolEngine] OTP required: {approval.ToolFullName}";
        var body = $"""
            A CRITICAL risk tool invocation requires OTP confirmation.

            Tool:         {approval.ToolFullName}
            Risk:         {approval.Risk}
            Reason:       {approval.ApprovalReason}
            InvocationId: {approval.Id}

            Your OTP: {otp}

            Submit via:   POST {_options.BaseUrl}/approvals/otp/verify
            This OTP expires at {approval.ExpiresAt:u}.
            Do NOT share this OTP.
            """;

        _log.LogInformation(
            "Sending OTP approval email to {To} for {ToolFullName} (invocationId={Id})",
            to, approval.ToolFullName, approval.Id);

        await _email.SendAsync(to, subject, body, ct);
    }

    // Generates a 6-digit cryptographically random OTP.
    private static string GenerateOtp()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        var value = (BitConverter.ToUInt32(bytes, 0) % 1_000_000);
        return value.ToString("D6");
    }

    /// <summary>
    /// C1 — Produces a PBKDF2-HMAC-SHA256 hash of the OTP with a fresh random salt.
    /// Stored format: "{32-hex-salt}:{64-hex-key}" (97 chars total).
    /// A 6-digit OTP has only 10^6 possible values; an unsalted SHA-256 would be
    /// brute-forced offline in milliseconds from a DB snapshot.
    /// 100 000 PBKDF2 iterations raise the cost of a full-space attack to ~2 CPU-hours.
    /// </summary>
    public static string HashOtp(string otp)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key  = Rfc2898DeriveBytes.Pbkdf2(
            otp, salt, iterations: 100_000,
            HashAlgorithmName.SHA256, outputLength: 32);
        return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(key)}";
    }

    /// <summary>
    /// C2 — Verifies an OTP against a stored PBKDF2 hash using constant-time comparison.
    /// Extracts the embedded salt, re-derives the key, then uses
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> to prevent timing-oracle attacks.
    /// </summary>
    public static bool VerifyOtp(string otp, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;
        byte[] salt, expectedKey;
        try
        {
            salt        = Convert.FromHexString(parts[0]);
            expectedKey = Convert.FromHexString(parts[1]);
        }
        catch (FormatException) { return false; }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(
            otp, salt, iterations: 100_000,
            HashAlgorithmName.SHA256, outputLength: 32);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
