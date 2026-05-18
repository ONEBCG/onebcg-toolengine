namespace ToolEngine.Llm.Models;

using System.Text.Json;

public sealed record LlmToolCall(
    string      Id,          // provider-assigned call ID
    string      ToolName,    // sanitized tool name (dots replaced by __)
    JsonElement Arguments);  // parsed arguments JSON
