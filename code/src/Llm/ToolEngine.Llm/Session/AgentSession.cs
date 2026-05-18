namespace ToolEngine.Llm.Session;

using ToolEngine.Llm.Models;

public sealed class AgentSession
{
    private readonly List<LlmMessage> _messages = [];
    private LlmUsage _usage = LlmUsage.Zero;

    public string                   SessionId    { get; init; } = Guid.NewGuid().ToString();
    public bool                     IsSingleTurn { get; init; }
    public IReadOnlyList<LlmMessage> Messages    => _messages.AsReadOnly();
    public int                      TokensUsed   => _usage.TotalTokens;
    public LlmUsage                 TotalUsage   => _usage;

    public void AddMessage(LlmMessage message) => _messages.Add(message);

    public void RecordUsage(LlmUsage usage) =>
        _usage = new LlmUsage(
            _usage.InputTokens  + usage.InputTokens,
            _usage.OutputTokens + usage.OutputTokens,
            _usage.EstimatedCostUsd + usage.EstimatedCostUsd);
}
