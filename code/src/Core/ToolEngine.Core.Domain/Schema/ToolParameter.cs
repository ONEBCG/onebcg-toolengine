namespace ToolEngine.Core.Domain.Schema;

/// <summary>
/// Describes a single parameter in a tool's input or output schema.
/// Produces a strict JSON Schema fragment: required fields are non-nullable,
/// optional fields use nullable type. additionalProperties is always false.
/// </summary>
public sealed record ToolParameter(
    string  Name,
    // JSON Schema type: "string" | "integer" | "number" | "boolean" | "object" | "array"
    string  JsonSchemaType,
    string  Description,
    bool    Required    = true,
    object? Default     = null,
    // JSON Schema format hint: "date-time" | "uri" | "uuid" | "email"
    string? Format      = null,
    // Regex pattern for string validation.
    string? Pattern     = null,
    double? Minimum     = null,
    double? Maximum     = null,
    // When true, JSON Schema type becomes ["type", "null"]. Agents treat as optional.
    bool    Nullable    = false,
    // For array types, the JSON Schema type of each element.
    string? ItemsType   = null,
    // Allowed values (JSON Schema enum).
    IReadOnlyList<string>? Enum = null);
