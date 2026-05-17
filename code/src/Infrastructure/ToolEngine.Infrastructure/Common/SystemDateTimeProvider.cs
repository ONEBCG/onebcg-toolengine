namespace ToolEngine.Infrastructure.Common;

using ToolEngine.Core.Abstractions.Common;

internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
