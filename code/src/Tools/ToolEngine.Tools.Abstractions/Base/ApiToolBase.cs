namespace ToolEngine.Tools.Abstractions.Base;

using System.Net.Http;
using ToolEngine.Core.Abstractions.Security;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Base for HTTP API tools.
/// Named HttpClient = tool FullName. Polly policies are configured at registration.
/// Credentials fetched via ISecretVault per-invocation (Zero Trust).
/// Never store credentials in fields or constructor parameters.
/// </summary>
public abstract class ApiToolBase<TInput, TOutput>
    : IToolHandler<TInput, TOutput>
{
    private readonly IHttpClientFactory _httpFactory;
    protected readonly ISecretVault     Vault;

    protected ApiToolBase(IHttpClientFactory httpFactory, ISecretVault vault)
    {
        _httpFactory = httpFactory;
        Vault        = vault;
    }

    public abstract string    Namespace    { get; }
    public abstract string    Name         { get; }
    public          string    FullName     => $"{Namespace}.{Name}";
    public abstract string    Version      { get; }
    public abstract string    Description  { get; }
    public          ToolType  Type         => ToolType.Api;
    public abstract ToolSchema InputSchema  { get; }
    public abstract ToolSchema OutputSchema { get; }

    /// <summary>
    /// Creates a named HttpClient using the tool's FullName as the client name.
    /// Polly retry + circuit breaker configured at DI registration.
    /// </summary>
    protected HttpClient CreateClient() => _httpFactory.CreateClient($"{Namespace}.{Name}");

    public abstract Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request, CancellationToken ct = default);

    public async IAsyncEnumerable<ToolChunk<TOutput>> StreamAsync(
        ToolRequest<TInput> request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        var response = await ExecuteAsync(request, ct);
        if (response.Success)
            yield return new ToolChunk<TOutput>(
                response.CorrelationId, response.Data!, 0, IsFinal: true, "done");
    }
}
