namespace ToolEngine.Core.Domain.Constants;

/// <summary>
/// JWT claim name strings used when reading token payloads.
///
/// Centralisng these prevents silent mismatches between the code that issues a token
/// (auth server, TenantClaimsTransformer) and the code that reads it (endpoints, behaviors).
/// A renamed claim found only at runtime is a hard-to-reproduce auth failure.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>
    /// Identifies the tenant the principal belongs to.
    /// Stored lowercase (normalised by Tenant.Create) so case-sensitive DB lookups are safe.
    /// </summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// Identifies the caller type (Human, AiAgent, System) encoded in the token.
    /// Used by the pipeline to set CallerType on ExecuteToolCommand without trusting the request body.
    /// </summary>
    public const string CallerType = "caller_type";
}
