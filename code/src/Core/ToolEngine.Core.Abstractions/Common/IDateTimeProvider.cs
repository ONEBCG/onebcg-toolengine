namespace ToolEngine.Core.Abstractions.Common;

/// <summary>Abstraction over the system clock. Inject instead of using DateTime.UtcNow directly.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
