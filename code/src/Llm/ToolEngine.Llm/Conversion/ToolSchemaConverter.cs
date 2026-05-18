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

    private static string BuildDescription(ToolDescriptor d)
    {
        var sb = new StringBuilder();
        sb.Append(d.Metadata.InputSchema.Description);

        if (!string.IsNullOrWhiteSpace(d.Metadata.InputSchema.WhenToUse))
        {
            sb.AppendLine();
            sb.Append("When to use: ");
            sb.Append(d.Metadata.InputSchema.WhenToUse);
        }

        if (!string.IsNullOrWhiteSpace(d.Metadata.InputSchema.WhenNotToUse))
        {
            sb.AppendLine();
            sb.Append("When NOT to use: ");
            sb.Append(d.Metadata.InputSchema.WhenNotToUse);
        }

        return sb.ToString();
    }
}
