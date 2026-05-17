namespace ToolEngine.Core.Abstractions.Common;

public interface ICurrentUser
{
    string  UserId   { get; }
    string  TenantId { get; }
    string  Email    { get; }
    bool    IsAuthenticated { get; }
    bool    IsInRole(string role);
    bool    HasClaim(string type, string value);
}
