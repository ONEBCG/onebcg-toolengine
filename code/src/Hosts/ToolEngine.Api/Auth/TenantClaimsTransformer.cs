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

        if (principal.FindFirst("tenant_id") is not null)
            return Task.FromResult(principal);

        // Strip authentication if tenant_id is missing
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(anonymous);
    }
}
