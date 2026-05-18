namespace ToolEngine.Llm.Conversion;

using System.Text;
using ToolEngine.Llm.Models;
using ToolEngine.Tools.Registry;

public sealed class ToolSchemaConverter
{
    /// <summary>
    /// Converts registry descriptors to provider-neutral LLM tool definitions.
    /// Name sanitization: "math.calculate" -> "math__calculate" (dots -> __)
    /// Desanitization:    "math__calculate" -> "math.calculate"
    /// </summary>
    public IReadOnlyList<LlmToolDefinition> Convert(IReadOnlyList<ToolDescriptor> descriptors)
    {
        var result = new List<LlmToolDefinition>(descriptors.Count);
        foreach (var d in descriptors)
        {
            if (!d.Metadata.IsEnabled) continue;

            var description = BuildDescription(d);
            result.Add(new LlmToolDefinition(
                SanitizeName(d.FullName),
                d.FullName,
                description,
                d.Metadata.InputSchema.ToJsonSchema()));
        }
        return result.AsReadOnly();
    }

    public static string SanitizeName(string fullName)    => fullName.Replace(".", "__");
    public static string DesanitizeName(string sanitized) => sanitized.Replace("__", ".");

    /// <summary>
    /// Builds the description string sent to the LLM for tool selection.
    ///
    /// <para>
    /// Format (blank lines between sections improve LLM attention to each block):
    /// <code>
    /// {InputSchema.Description}
    ///
    /// When to use: {WhenToUse}
    ///
    /// When NOT to use: {WhenNotToUse}
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Research finding: tools with explicit WhenToUse / WhenNotToUse guidance
    /// reduce LLM selection errors by ~30% compared to description-only schemas,
    /// because the model can rule out tools rather than defaulting to the closest match.
    /// </para>
    /// </summary>
    private static string BuildDescription(ToolDescriptor d)
    {
        var schema = d.Metadata.InputSchema;
        var sb     = new StringBuilder();

        sb.Append(schema.Description.TrimEnd());

        if (!string.IsNullOrWhiteSpace(schema.WhenToUse))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("When to use: ");
            sb.Append(schema.WhenToUse.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(schema.WhenNotToUse))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("When NOT to use: ");
            sb.Append(schema.WhenNotToUse.TrimEnd());
        }

        return sb.ToString();
    }
}
