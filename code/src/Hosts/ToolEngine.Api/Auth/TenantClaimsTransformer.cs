namespace ToolEngine.Api.Auth;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using ToolEngine.Core.Domain.Constants;

/// <summary>
/// Validates that every authenticated request carries a tenant_id claim.
/// Requests without this claim are treated as unauthenticated so downstream
/// code never receives a principal that has no tenant context.
/// </summary>
public sealed class TenantClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Task.FromResult(principal);

        var tenantClaim = principal.FindFirst(JwtClaimNames.TenantId);
        if (tenantClaim is null)
        {
            // Strip authentication if tenant_id is missing.
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            return Task.FromResult(anonymous);
        }

        // Tenant IDs are stored lowercase via Tenant.Create().
        // Normalise the claim value so "Acme" and "acme" resolve to the same row
        // rather than producing a spurious 401 on a valid token with a mixed-case claim.
        var normalized = tenantClaim.Value.Trim().ToLowerInvariant();
        if (normalized == tenantClaim.Value)
            return Task.FromResult(principal); // already canonical — no copy needed

        var identity = new ClaimsIdentity(
            principal.Claims.Select(c =>
                c.Type == JwtClaimNames.TenantId
                    ? new Claim(JwtClaimNames.TenantId, normalized, c.ValueType, c.Issuer)
                    : c),
            principal.Identity.AuthenticationType);

        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}
