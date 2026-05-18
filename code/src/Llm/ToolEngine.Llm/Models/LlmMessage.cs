namespace ToolEngine.Llm.Models;

public sealed class LlmMessage
{
    public MessageRole  Role        { get; init; }
    public string?      Content     { get; init; }   // text content (User / Assistant / Tool result)
    public LlmToolCall? ToolCall    { get; init; }   // set on Role.Assistant tool_use messages
    public string?      ToolCallId  { get; init; }   // set on Role.Tool result messages

    public static LlmMessage User(string text)      => new() { Role = MessageRole.User,      Content = text };
    public static LlmMessage System(string text)    => new() { Role = MessageRole.System,    Content = text };
    public static LlmMessage Assistant(string text) => new() { Role = MessageRole.Assistant, Content = text };

    public static LlmMessage AssistantToolUse(LlmToolCall call) =>
        new() { Role = MessageRole.Assistant, ToolCall = call };

    public static LlmMessage ToolResult(string toolCallId, string result) =>
        new() { Role = MessageRole.Tool, ToolCallId = toolCallId, Content = result };
}
