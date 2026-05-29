using ToolEngine.Core.Abstractions.Common;

namespace ToolEngine.Infrastructure.Common;

// ── SystemDateTimeProvider ────────────────────────────────────────────────────

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
