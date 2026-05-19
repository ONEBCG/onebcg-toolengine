namespace ToolEngine.Llm.Prompts;

using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Loads prompt strings from <c>prompts.json</c> at startup and caches them in an
/// immutable frozen dictionary for lock-free reads thereafter.
///
/// The JSON file is resolved relative to the executing assembly's directory so it is
/// copied to the output folder by the build system (see ToolEngine.Llm.csproj).
/// Prompts are loaded once at construction — to pick up changes, restart the process.
/// Hot-reload via IOptionsMonitor or file watcher is a future enhancement.
///
/// Fail-fast: if the file is missing or a required key is absent, the process throws
/// at startup rather than at the first LLM call. Silent degradation (empty system prompt)
/// is harder to diagnose than a startup exception.
/// </summary>
public sealed class JsonPromptStore : IPromptStore
{
    private readonly FrozenDictionary<string, string> _prompts;
    private readonly ILogger<JsonPromptStore>         _logger;

    public JsonPromptStore(ILogger<JsonPromptStore> logger)
    {
        _logger = logger;
        _prompts = Load();
        _logger.LogInformation("JsonPromptStore loaded {Count} prompts.", _prompts.Count);
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        if (_prompts.TryGetValue(key, out var value))
            return value;

        throw new KeyNotFoundException(
            $"Prompt key '{key}' was not found in prompts.json. " +
            "Add the key or use GetOrDefault for optional prompts.");
    }

    /// <inheritdoc />
    public string GetOrDefault(string key, string defaultValue)
        => _prompts.TryGetValue(key, out var value) ? value : defaultValue;

    // ── Private ───────────────────────────────────────────────────────────────

    private static FrozenDictionary<string, string> Load()
    {
        var searchDirs = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(JsonPromptStore).Assembly.Location) ?? "."
        };

        string? filePath = null;
        foreach (var dir in searchDirs)
        {
            var candidate = Path.Combine(dir, "prompts.json");
            if (File.Exists(candidate))
            {
                filePath = candidate;
                break;
            }
        }

        if (filePath is null)
            throw new FileNotFoundException(
                "prompts.json not found. Ensure it is set to 'Copy to Output Directory: Always' " +
                "in ToolEngine.Llm.csproj. Searched: " + string.Join(", ", searchDirs));

        using var stream = File.OpenRead(filePath);
        using var doc    = JsonDocument.Parse(stream);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Flatten(doc.RootElement, prefix: "", dict);
        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recursively flattens a JSON object into dot-separated key/value pairs.
    /// <c>{ "agent": { "grounding-reminder": "..." } }</c> becomes
    /// <c>"agent.grounding-reminder" → "..."</c>.
    /// </summary>
    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> dict)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

            if (prop.Value.ValueKind == JsonValueKind.Object)
                Flatten(prop.Value, key, dict);
            else if (prop.Value.ValueKind == JsonValueKind.String)
                dict[key] = prop.Value.GetString() ?? string.Empty;
        }
    }
}
