using System.Security.Cryptography;
using System.Text;

namespace ToolEngine.Payment.Application.Commands;

/// <summary>
/// Stateless HMAC-SHA256 token generator for payment resume verification.
/// Tokens are bound to a 5-minute time window. Each token is valid for
/// the window it was generated in PLUS the previous window (~10 minutes max),
/// preventing tokens from expiring at a window boundary.
/// No database storage required — tokens are computed deterministically.
/// </summary>
internal static class VerificationTokenHelper
{
    private const int WindowMinutes = 5;

    /// <summary>Generates a verification token for the given payment details.</summary>
    public static string Generate(Guid paymentId, decimal amount, string currency, string secret)
    {
        var windowKey = GetWindowKey(DateTimeOffset.UtcNow);
        return ComputeHmac(paymentId, amount, currency, secret, windowKey);
    }

    /// <summary>
    /// Validates a token against the current AND previous 5-minute window.
    /// Returns true if either window matches.
    /// </summary>
    public static bool Validate(string token, Guid paymentId, decimal amount, string currency, string secret)
    {
        var now = DateTimeOffset.UtcNow;

        var current  = ComputeHmac(paymentId, amount, currency, secret, GetWindowKey(now));
        if (ConstantTimeEquals(token, current)) return true;

        var previous = ComputeHmac(paymentId, amount, currency, secret, GetWindowKey(now.AddMinutes(-WindowMinutes)));
        return ConstantTimeEquals(token, previous);
    }

    private static string ComputeHmac(Guid paymentId, decimal amount, string currency, string secret, string windowKey)
    {
        var message  = $"{paymentId}:{amount:F2}:{currency.ToUpperInvariant()}:{windowKey}";
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var hash     = HMACSHA256.HashData(keyBytes, msgBytes);
        // First 32 chars of base64 = 192 bits, sufficient for a short-lived token
        return Convert.ToBase64String(hash)[..32];
    }

    // Floor UTC timestamp to the nearest WindowMinutes boundary
    private static string GetWindowKey(DateTimeOffset t)
    {
        var minute = (t.Minute / WindowMinutes) * WindowMinutes;
        return $"{t.Year:0000}{t.Month:00}{t.Day:00}{t.Hour:00}{minute:00}";
    }

    // Constant-time comparison prevents timing attacks on token equality checks
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }
}
