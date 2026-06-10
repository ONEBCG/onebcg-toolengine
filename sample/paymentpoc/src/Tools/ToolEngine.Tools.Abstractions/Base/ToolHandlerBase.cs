using System.Text.Json;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Tools.Abstractions.Base;

// ── Shared base ──────────────────────────────────────────────────────────────

public abstract class ToolHandlerBase
{
    public abstract string    Namespace { get; }
    public abstract string    Name      { get; }
    public abstract string    Version   { get; }
    public abstract ToolSchema Schema   { get; }

    public string FullName => $"{Namespace}.{Name}";

    protected static JsonElement BuildJsonSchema<T>()
    {
        var props = typeof(T).GetProperties()
            .ToDictionary(
                p => JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                p => (object)MapClrProperty(p.PropertyType));

        // Required = all non-nullable properties (Guid? / string? / decimal? are optional)
        var required = typeof(T).GetProperties()
            .Where(p => !IsNullable(p.PropertyType))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToArray();

        var schema = new
        {
            type       = "object",
            properties = props,
            required,
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    /// <summary>
    /// Returns a JSON Schema property descriptor for the given CLR type.
    /// Enums are emitted as { "type": "string", "enum": ["Value1", "Value2", ...] }
    /// so the LLM receives the list of valid string values and never needs to guess.
    /// </summary>
    private static object MapClrProperty(Type t)
    {
        // Unwrap Nullable<T> — Guid?, decimal?, int?, enum?
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null) return MapClrProperty(underlying);

        // Enum → string with allowed values list
        if (t.IsEnum)
        {
            var values = Enum.GetNames(t);
            return new { type = "string", @enum = values };
        }

        return new { type = MapClrType(t) };
    }

    private static string MapClrType(Type t)
    {
        if (t == typeof(string))                               return "string";
        if (t == typeof(Guid))                                 return "string";
        if (t == typeof(decimal) || t == typeof(double)
         || t == typeof(float))                                return "number";
        if (t == typeof(int)     || t == typeof(long))         return "integer";
        if (t == typeof(bool))                                 return "boolean";
        if (t == typeof(DateTimeOffset) || t == typeof(DateTime)) return "string";
        if (t.IsArray || (t.IsGenericType
         && t.GetGenericTypeDefinition() == typeof(List<>)))   return "array";
        return "object";
    }

    private static bool IsNullable(Type t) =>
        Nullable.GetUnderlyingType(t) is not null ||
        (t.IsClass && t != typeof(string));   // reference types are nullable but string is always required

    public ToolDescriptor ToDescriptor(ToolType toolType, Type handlerType) =>
        new()
        {
            Namespace   = Namespace,
            Name        = Name,
            Version     = Version,
            Type        = toolType,
            IsEnabled   = true,
            HandlerType = handlerType,
            Schema      = Schema,
        };
}

// ── LogicToolBase — pure computation, no I/O ─────────────────────────────────

public abstract class LogicToolBase<TInput, TOutput>
    : ToolHandlerBase, IToolHandler<TInput, TOutput>
{
    public Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request, CancellationToken ct = default) =>
        HandleAsync(request, ct);

    protected abstract Task<ToolResponse<TOutput>> HandleAsync(
        ToolRequest<TInput> request, CancellationToken ct);
}

// ── ApiToolBase — outbound HTTP calls ────────────────────────────────────────

public abstract class ApiToolBase<TInput, TOutput>
    : ToolHandlerBase, IToolHandler<TInput, TOutput>
{
    protected readonly IHttpClientFactory HttpClientFactory;

    protected ApiToolBase(IHttpClientFactory httpClientFactory) =>
        HttpClientFactory = httpClientFactory;

    public Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request, CancellationToken ct = default) =>
        HandleAsync(request, ct);

    protected abstract Task<ToolResponse<TOutput>> HandleAsync(
        ToolRequest<TInput> request, CancellationToken ct);

    protected HttpClient CreateClient(string name = "default") =>
        HttpClientFactory.CreateClient(name);
}

// ── DatabaseToolBase — read/write via IUnitOfWork ────────────────────────────

public abstract class DatabaseToolBase<TInput, TOutput>
    : ToolHandlerBase, IToolHandler<TInput, TOutput>
{
    protected readonly IUnitOfWork UnitOfWork;

    protected DatabaseToolBase(IUnitOfWork unitOfWork) =>
        UnitOfWork = unitOfWork;

    public Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request, CancellationToken ct = default) =>
        HandleAsync(request, ct);

    protected abstract Task<ToolResponse<TOutput>> HandleAsync(
        ToolRequest<TInput> request, CancellationToken ct);
}

// ── CompositeToolBase — orchestrates other tools via IToolExecutor ────────────

public abstract class CompositeToolBase<TInput, TOutput>
    : ToolHandlerBase, IToolHandler<TInput, TOutput>
{
    protected readonly IToolExecutor ToolExecutor;

    protected CompositeToolBase(IToolExecutor toolExecutor) =>
        ToolExecutor = toolExecutor;

    public Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request, CancellationToken ct = default) =>
        HandleAsync(request, ct);

    protected abstract Task<ToolResponse<TOutput>> HandleAsync(
        ToolRequest<TInput> request, CancellationToken ct);
}
