namespace ToolEngine.Infrastructure.Common;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ToolEngine.Core.Abstractions.Common;

internal sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) =>
        _accessor = accessor;

    private ClaimsPrincipal? Principal =>
        _accessor.HttpContext?.User;

    public string UserId   => Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
    public string TenantId => Principal?.FindFirst("tenant_id")?.Value               ?? string.Empty;
    public string Email    => Principal?.FindFirst(ClaimTypes.Email)?.Value          ?? string.Empty;
    public bool   IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role)           => Principal?.IsInRole(role)         ?? false;
    public bool HasClaim(string type, string value) => Principal?.HasClaim(type, value) ?? false;
}
