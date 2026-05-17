namespace ToolEngine.Api.Auth;

public sealed class JwtSettings
{
    public string Issuer   { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string Secret   { get; init; } = string.Empty;
}
