namespace ToolEngine.Api.Auth;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Validates that every authenticated request carries a tenant_id claim.
/// Requests without this claim are treated as unauthenticated.
/// </summary>
public sealed class TenantClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Task.FromResult(principal);

        var tenantClaim = principal.FindFirst("tenant_id");
        if (tenantClaim is null)
        {
            // Strip authentication if tenant_id is missing.
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            return Task.FromResult(anonymous);
        }

        // C4 — Tenant IDs are stored lowercase via Tenant.Create().
        // Normalise the claim value so "Acme" and "acme" resolve to the same row
        // rather than producing a spurious 401 on a valid token.
        var normalized = tenantClaim.Value.Trim().ToLowerInvariant();
        if (normalized == tenantClaim.Value)
            return Task.FromResult(principal); // already canonical — no copy needed

        var identity = new ClaimsIdentity(
            principal.Claims.Select(c =>
                c.Type == "tenant_id"
                    ? new Claim("tenant_id", normalized, c.ValueType, c.Issuer)
                    : c),
            principal.Identity.AuthenticationType);

        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}
