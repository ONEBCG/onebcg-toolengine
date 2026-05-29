using ToolEngine.Core.Abstractions.Secrets;

namespace ToolEngine.Infrastructure.Secrets;

// ── NullSecretVault ───────────────────────────────────────────────────────────
// POC implementation — returns null for all secrets.
// In production: replace with Azure Key Vault / AWS Secrets Manager.

public sealed class NullSecretVault : ISecretVault
{
    public Task<string?> GetSecretAsync(
        string scope, string name, string key, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
