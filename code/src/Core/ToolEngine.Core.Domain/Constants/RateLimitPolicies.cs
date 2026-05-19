namespace ToolEngine.Core.Domain.Constants;

/// <summary>
/// Rate-limiter policy name strings registered in the ASP.NET Core rate-limiter middleware.
///
/// The policy name ties the registration in Program.cs to the <c>RequireRateLimiting</c> call
/// on the endpoint. A mismatch produces no compile-time error — the endpoint simply runs without
/// rate limiting at runtime. Keeping policy names here makes both sides of the contract explicit.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Sliding-window policy applied to POST /approvals/otp/verify.
    /// Allows 10 attempts per IP per 10-minute window to limit brute-force OTP attacks
    /// (OWASP MFA Cheat Sheet). A per-entity failed-attempt counter handles targeted
    /// attacks against a specific approval token.
    /// </summary>
    public const string OtpVerify = "otp-verify";
}
