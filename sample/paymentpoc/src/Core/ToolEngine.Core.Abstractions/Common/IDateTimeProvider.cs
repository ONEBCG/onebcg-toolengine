namespace ToolEngine.Core.Abstractions.Common;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
