using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Tags("Auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config) => _config = config;

    /// <summary>Exchange a Google ID token for an application JWT.</summary>
    /// <remarks>
    /// The browser obtains a Google ID token via the Google Identity Services library,
    /// then POSTs it here. This endpoint validates the token against Google's public keys,
    /// enforces the configured domain restriction, and issues a short-lived app JWT
    /// signed with the same symmetric key used by all other API endpoints.
    /// </remarks>
    [HttpPost("auth/google")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.IdToken))
            return BadRequest(new { error = "id_token_required", message = "Google ID token is required." });

        var clientId = _config["Auth:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(503, new
            {
                error   = "google_not_configured",
                message = "Google Sign-In is not configured on this server. Set Auth:Google:ClientId in appsettings.",
            });

        // Validate Google-issued token — signature, expiry, and audience
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId],
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken, settings);
        }
        catch (InvalidJwtException ex)
        {
            return Unauthorized(new { error = "invalid_google_token", message = ex.Message });
        }

        // Domain restriction — server-side enforcement (client-side check is UI-only)
        var allowedDomain = _config["Auth:AllowedDomain"] ?? "onebcg.com";
        if (!payload.Email.EndsWith($"@{allowedDomain}", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new
            {
                error   = "domain_not_allowed",
                message = $"Access is restricted to @{allowedDomain} accounts. Your account ({payload.Email}) is not authorised.",
            });
        }

        // Issue application JWT using the same symmetric key as all other endpoints
        var jwtSecret = _config["Jwt:SecretKey"]!;
        var key       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds     = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   payload.Subject),
            new Claim(JwtRegisteredClaimNames.Email, payload.Email),
            new Claim("name",    payload.Name    ?? payload.Email),
            new Claim("picture", payload.Picture ?? string.Empty),
        };

        var token = new JwtSecurityToken(
            issuer:             _config["Jwt:Issuer"],
            audience:           _config["Jwt:Audience"],
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return Ok(new
        {
            token   = new JwtSecurityTokenHandler().WriteToken(token),
            expires = token.ValidTo,
            user = new
            {
                email   = payload.Email,
                name    = payload.Name,
                picture = payload.Picture,
            },
        });
    }
}

public sealed record GoogleSignInRequest(string? IdToken);
