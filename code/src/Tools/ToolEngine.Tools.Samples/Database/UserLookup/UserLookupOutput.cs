namespace ToolEngine.Tools.Samples.Database.UserLookup;

public sealed record UserLookupOutput(
    Guid   UserId,
    string Email,
    string DisplayName,
    string TenantId,
    bool   IsActive);
