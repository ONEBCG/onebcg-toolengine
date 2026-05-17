namespace ToolEngine.Core.Abstractions.Security;

/// <summary>
/// Zero Trust secret store. Issues short-lived credentials scoped to a single
/// CorrelationId. Tools call GetSecretAsync inside ExecuteAsync — never receive
/// credentials via ToolRequest, Metadata, or constructor injection.
/// </summary>
public interface ISecretVault
{
    /// <summary>
    /// Returns a credential scoped to (toolNamespace + toolName + correlationId).
    /// The credential is valid only for the lifetime of that correlation.
    /// </summary>
    Task<Secret> GetSecretAsync(
        string            toolNamespace,
        string            toolName,
        string            secretName,
        Guid              correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes all credentials issued for this correlationId.
    /// Called by the executor on success, failure, timeout, or guard block.
    /// </summary>
    Task RevokeAsync(Guid correlationId, CancellationToken ct = default);
}

public sealed record Secret(
    string Value,
    DateTimeOffset ExpiresAt,
    string SecretName,
    Guid   CorrelationId)
{
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}
