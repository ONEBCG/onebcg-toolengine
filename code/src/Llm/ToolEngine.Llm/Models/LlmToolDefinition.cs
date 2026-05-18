namespace ToolEngine.Llm.Models;

public sealed record LlmToolDefinition(
    string SanitizedName,    // "math__calculate" (dots → __)
    string OriginalFullName, // "math.calculate"
    string Description,      // includes WhenToUse / WhenNotToUse
    string InputSchemaJson); // JSON Schema string from ToolSchema.ToJsonSchema()
