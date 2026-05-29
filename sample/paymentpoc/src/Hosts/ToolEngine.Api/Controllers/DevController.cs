using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Tags("Dev")]
public sealed class DevController : ControllerBase
{
    private readonly IConfiguration      _config;
    private readonly IWebHostEnvironment _env;

    public DevController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env    = env;
    }

    /// <summary>[DEV ONLY] Generate a JWT for testing without an IdP.</summary>
    [HttpPost("dev/token")]
    [AllowAnonymous]
    public IActionResult GenerateDevToken([FromBody] DevTokenRequest req)
    {
        if (!_env.IsDevelopment())
            return Forbid();

        var jwtSecret = _config["Jwt:SecretKey"]!;
        var key       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds     = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims    = new[]
        {
            new Claim("sub",  req.UserId   ?? "dev-user-001"),
            new Claim("name", req.UserName ?? "Dev User"),
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
            warning = "Dev token endpoint — never expose in production.",
        });
    }
}

public sealed record DevTokenRequest(string? UserId, string? UserName);
