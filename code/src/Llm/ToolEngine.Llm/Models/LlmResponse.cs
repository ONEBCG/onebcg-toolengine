namespace ToolEngine.Llm.Models;

public sealed record LlmResponse(
    StopReason   StopReason,
    string?      Content,    // text reply (when StopReason == EndTurn)
    LlmToolCall? ToolCall,   // tool selection (when StopReason == ToolUse)
    LlmUsage     Usage,
    string?      ErrorMessage = null);
