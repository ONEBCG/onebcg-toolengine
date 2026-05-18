namespace ToolEngine.Llm.Models;

public sealed record LlmUsage(int InputTokens, int OutputTokens, decimal EstimatedCostUsd = 0m)
{
    public int TotalTokens => InputTokens + OutputTokens;
    public static LlmUsage Zero => new(0, 0, 0m);
}
