namespace ToolEngine.Application.Tests.Helpers;

using MediatR;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Factory methods for MediatR RequestHandlerDelegate&lt;TResponse&gt; used in pipeline behavior tests.
/// MediatR 12 changed the delegate to Func&lt;CancellationToken, Task&lt;TResponse&gt;&gt;.
/// </summary>
public static class FakeDelegates
{
    /// <summary>Returns a delegate that resolves to <paramref name="value"/>.</summary>
    public static RequestHandlerDelegate<T> Success<T>(T value) =>
        _ => Task.FromResult(value);

    /// <summary>Returns a delegate that throws an exception with the given message.</summary>
    public static RequestHandlerDelegate<T> Fail<T>(string message) =>
        _ => throw new Exception(message);

    /// <summary>
    /// Returns a delegate that resolves to a successful ToolResponse&lt;object&gt;.
    /// </summary>
    public static RequestHandlerDelegate<ToolResponse<object>> SuccessResponse(Guid correlationId) =>
        _ => Task.FromResult(ToolResponse<object>.Ok(correlationId, new object()));

    /// <summary>
    /// Returns a delegate that resolves to a failed ToolResponse&lt;object&gt;.
    /// </summary>
    public static RequestHandlerDelegate<ToolResponse<object>> FailResponse(
        Guid correlationId, string errorCode) =>
        _ => Task.FromResult(
            ToolResponse<object>.Fail(correlationId, new ToolError(errorCode, "desc", 400)));
}

/// <summary>
/// Fixed-clock implementation of IDateTimeProvider for deterministic tests.
/// </summary>
public sealed class FakeClock : IDateTimeProvider
{
    public static readonly DateTimeOffset FixedTime =
        new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => FixedTime;
}

/// <summary>
/// Test helper to create Tenant aggregate instances without the EF Core materializer.
/// </summary>
public static class TenantBuilder
{
    private static readonly FakeClock Clock = new();

    /// <summary>
    /// Creates an active tenant. Calls AllowNamespace for each namespace in
    /// <paramref name="allowedNamespaces"/> (default ["*"]).
    /// </summary>
    public static Tenant Active(
        string    id                = "test-tenant",
        string[]? allowedNamespaces = null)
    {
        var namespaces = allowedNamespaces ?? ["*"];

        var result = Tenant.Create(id, "Test Tenant", "test", Clock);
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"TenantBuilder.Active failed: {result.Error.Description}");

        var tenant = result.Value;
        foreach (var ns in namespaces)
            tenant.AllowNamespace(ns);

        return tenant;
    }

    /// <summary>Creates an inactive tenant (calls Deactivate after creation).</summary>
    public static Tenant Inactive(string id = "inactive-tenant")
    {
        var tenant = Active(id);
        tenant.Deactivate("test", Clock);
        return tenant;
    }
}
