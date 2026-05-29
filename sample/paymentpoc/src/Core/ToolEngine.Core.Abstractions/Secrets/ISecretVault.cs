namespace ToolEngine.Core.Abstractions.Secrets;

public interface ISecretVault
{
    Task<string?> GetSecretAsync(string scope, string name, string key, CancellationToken ct = default);
}
