namespace ToolEngine.Api.Endpoints;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ToolEngine.Api.Auth;
using ToolEngine.Core.Domain.Constants;

public static class DevEndpoints
{
    public static WebApplication MapDevEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;

        app.MapGet("/dev/token", GenerateDevToken)
           .WithTags("Dev")
           .WithSummary("Generate a short-lived JWT for local testing. Development only — never exposed in production.")
           .AllowAnonymous();

        return app;
    }

    private static IResult GenerateDevToken(
        JwtSettings jwt,
        // Accept an optional tenant parameter so dev tokens are not hard-coded to "onebcg-default-tenant".
        // Value is lowercased to match the normalisation applied by Tenant.Create().
        [Microsoft.AspNetCore.Mvc.FromQuery] string? tenant = null)
    {
        var tenantId = (tenant ?? "onebcg-default-tenant").Trim().ToLowerInvariant();

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "dev-user"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.TenantId, tenantId),
        };

        var token = new JwtSecurityToken(
            issuer:             jwt.Issuer,
            audience:           jwt.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return Results.Ok(new
        {
            token     = new JwtSecurityTokenHandler().WriteToken(token),
            expiresIn = 28800,
            tenantId,
        });
    }
}
