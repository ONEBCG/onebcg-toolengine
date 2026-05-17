namespace ToolEngine.Application.Tests.Helpers;

using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Fluent builder for ExecuteToolCommand&lt;object, object&gt; with sensible test defaults.
/// </summary>
public sealed class CommandBuilder
{
    private Guid      _correlationId         = Guid.NewGuid();
    private string    _tenantId              = "test-tenant";
    private string    _userId                = "test-user";
    private string    _toolName              = "calculate";
    private string    _toolVersion           = "1.0";
    private string    _toolNamespace         = "math";
    private int       _maxResponseTokens     = 25_000;
    private string?   _idempotencyKey        = null;
    private CallerType _callerType           = CallerType.Human;
    private string?   _governanceMetadataJson = null;

    public CommandBuilder WithCorrelationId(Guid id)
    {
        _correlationId = id;
        return this;
    }

    public CommandBuilder WithTenantId(string tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    public CommandBuilder WithToolNamespace(string ns)
    {
        _toolNamespace = ns;
        return this;
    }

    public CommandBuilder WithToolName(string name)
    {
        _toolName = name;
        return this;
    }

    public CommandBuilder WithMaxResponseTokens(int tokens)
    {
        _maxResponseTokens = tokens;
        return this;
    }

    public CommandBuilder WithIdempotencyKey(string key)
    {
        _idempotencyKey = key;
        return this;
    }

    public CommandBuilder WithCallerType(CallerType callerType)
    {
        _callerType = callerType;
        return this;
    }

    public CommandBuilder WithGovernanceMetadataJson(string json)
    {
        _governanceMetadataJson = json;
        return this;
    }

    public ExecuteToolCommand<object, object> Build() =>
        new(
            CorrelationId:         _correlationId,
            TenantId:              _tenantId,
            UserId:                _userId,
            ToolName:              _toolName,
            ToolVersion:           _toolVersion,
            Input:                 new object(),
            ToolType:              ToolType.Logic,
            ToolNamespace:         _toolNamespace,
            MaxResponseTokens:     _maxResponseTokens,
            IdempotencyKey:        _idempotencyKey,
            CallerType:            _callerType,
            GovernanceMetadataJson: _governanceMetadataJson);

    /// <summary>
    /// Returns a success ToolResponse&lt;object&gt; for use as the next() return value in tests.
    /// </summary>
    public static ToolResponse<object> BuildResponse(Guid correlationId) =>
        ToolResponse<object>.Ok(correlationId, new object());
}
