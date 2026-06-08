namespace ToolEngine.Payment.Application.Commands;

/// <summary>Typed configuration for the Payment application layer.</summary>
public sealed class PaymentApplicationOptions
{
    public const string Section = "Payment";

    /// <summary>
    /// HMAC secret used to sign and verify payment resume tokens.
    /// Override in production via environment variable or secrets manager.
    /// </summary>
    public string ResumeVerificationSecret { get; init; } = "one-bcg-resume-verify-key-2026";
}
