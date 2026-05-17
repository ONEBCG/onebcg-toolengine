namespace ToolEngine.Core.Domain.Schema;

using System.Text.Json;

/// <summary>
/// MCP-aligned tool schema. Describes input or output shape for both humans
/// and agents. Produces strict JSON Schema (draft-2020-12, additionalProperties=false).
///
/// Per Anthropic 2025 engineering guidance:
///   - Description:   what the tool does (one factual sentence).
///   - WhenToUse:     concrete trigger conditions.
///   - WhenNotToUse:  explicit negative guidance — reduces hallucination.
///   - Examples:      at least one concrete input/output pair.
/// </summary>
public sealed record ToolSchema(
    string                       TypeName,
    string                       Description,
    string                       WhenToUse,
    string                       WhenNotToUse,
    IReadOnlyList<ToolParameter> Parameters,
    IReadOnlyList<ToolExample>   Examples)
{
    /// <summary>Full schema with WhenToUse, WhenNotToUse, and Examples.</summary>
    public static ToolSchema For<T>(
        string description,
        string whenToUse,
        string whenNotToUse,
        ToolExample[] examples,
        params ToolParameter[] parameters) =>
        new(typeof(T).Name, description, whenToUse, whenNotToUse, parameters, examples);

    /// <summary>Minimal schema overload — WhenToUse and Examples left empty.</summary>
    public static ToolSchema For<T>(
        string description,
        params ToolParameter[] parameters) =>
        new(typeof(T).Name, description, string.Empty, string.Empty, parameters, []);

    public static ToolSchema Empty =>
        new("void", "No schema defined.", string.Empty, string.Empty, [], []);

    /// <summary>
    /// Produces a strict JSON Schema object (draft-2020-12).
    /// additionalProperties: false
    /// All required fields are non-nullable.
    /// Optional fields use ["type", "null"] union.
    /// </summary>
    public string ToJsonSchema()
    {
        var required   = Parameters.Where(p => p.Required && !p.Nullable)
                                   .Select(p => p.Name)
                                   .ToList();
        var properties = new Dictionary<string, object>();

        foreach (var p in Parameters)
        {
            var prop = new Dictionary<string, object>
            {
                ["description"] = p.Description
            };

            if (p.Nullable)
                prop["type"] = new[] { p.JsonSchemaType, "null" };
            else
                prop["type"] = p.JsonSchemaType;

            if (p.Format    is not null) prop["format"]  = p.Format;
            if (p.Pattern   is not null) prop["pattern"] = p.Pattern;
            if (p.Minimum   is not null) prop["minimum"] = p.Minimum;
            if (p.Maximum   is not null) prop["maximum"] = p.Maximum;
            if (p.Default   is not null) prop["default"] = p.Default;
            if (p.ItemsType is not null)
                prop["items"] = new Dictionary<string, object> { ["type"] = p.ItemsType };
            if (p.Enum is { Count: > 0 })
                prop["enum"] = p.Enum;

            properties[p.Name] = prop;
        }

        var schema = new Dictionary<string, object>
        {
            ["type"]                 = "object",
            ["description"]          = Description,
            ["properties"]           = properties,
            ["required"]             = required,
            ["additionalProperties"] = false
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
