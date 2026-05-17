namespace ToolEngine.Api.Endpoints;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ToolEngine.Api.Auth;

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

    private static IResult GenerateDevToken(JwtSettings jwt)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "dev-user"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("tenant_id", "acme-corp"),
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
            tenantId  = "acme-corp",
        });
    }
}
