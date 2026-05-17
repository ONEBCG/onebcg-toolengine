namespace ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Marker interface for tools that natively produce token-by-token streams
/// (e.g. LLM wrappers). Allows the executor to skip the single-chunk wrapper.
/// </summary>
public interface IStreamingToolHandler<TInput, TOutput>
    : IToolHandler<TInput, TOutput>;
