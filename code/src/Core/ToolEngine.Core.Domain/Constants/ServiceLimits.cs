namespace ToolEngine.Core.Domain.Constants;

/// <summary>
/// Numeric service limits that appear in more than one layer of the pipeline.
///
/// Keeping them here prevents the same magic number from being independently maintained
/// in the behavior, the endpoint, and tests — all three must move together when a limit changes.
/// Change a value here and it propagates everywhere at compile time.
/// </summary>
public static class ServiceLimits
{
    // ── Agent / LLM ───────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum characters accepted in a single /agent/chat request body.
    /// Limits the prompt-injection surface and caps per-request token cost before the
    /// LLM is ever called. 4 000 chars ~ 1 000–1 500 tokens at typical English density.
    /// </summary>
    public const int AgentMaxTextLength = 4_000;

    /// <summary>
    /// Maximum tool invocations within a single agent correlation context before
    /// the loop-detection circuit opens. Prevents runaway cost from recursive agent calls.
    /// </summary>
    public const int LoopDetectionMaxCallsPerCorrelation = 10;

    /// <summary>TTL for the loop-detection cache key, in minutes. One per agent turn.</summary>
    public const int LoopDetectionTtlMinutes = 10;

    // ── Approval / OTP ────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum failed OTP attempts before the PendingApproval is permanently expired.
    /// Matches OWASP MFA Cheat Sheet lockout recommendations.
    /// </summary>
    public const int OtpMaxFailedAttempts = 5;

    /// <summary>
    /// Sliding window duration (minutes) for the OTP rate-limiter policy.
    /// 10 attempts per IP within this window (OWASP brute-force mitigation).
    /// </summary>
    public const int OtpRateLimitWindowMinutes = 10;

    /// <summary>Maximum OTP verification attempts allowed within the rate-limit window.</summary>
    public const int OtpRateLimitPermitLimit = 10;

    // ── Outbox ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum number of delivery attempts for an outbox notification message before
    /// it is abandoned. Each retry doubles the back-off delay.
    /// </summary>
    public const int OutboxMaxRetries = 5;

    // ── Grounding observability ───────────────────────────────────────────────

    /// <summary>
    /// Reply-to-tool-result character length ratio above which a grounding warning is logged.
    /// A reply more than 5× longer than the tool result likely contains knowledge not
    /// derived from the tool output — operators should review for leakage.
    /// </summary>
    public const int GroundingLengthRatioWarningThreshold = 5;
}
