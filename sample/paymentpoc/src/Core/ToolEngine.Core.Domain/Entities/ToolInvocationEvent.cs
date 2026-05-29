using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Core.Domain.Entities;

// APPEND-ONLY — no update methods. SOC 2 CC7 immutable audit log (H1).
// DB grants: INSERT only on this table — no UPDATE/DELETE (runbook requirement).
public sealed class ToolInvocationEvent : Entity<Guid>
{
    public Guid            InvocationRecordId     { get; private set; }
    public string          EventType              { get; private set; } = default!; // "Invoked"|"Succeeded"|"Failed"|"Suspended"
    public DateTimeOffset  OccurredAt             { get; private set; }
    public long?           DurationMs             { get; private set; }

    // H4 — CallerType on every event row (not just the record)
    public CallerType      CallerType             { get; private set; }

    // H5 — GovernanceMetadataJson on every event row
    public string?         GovernanceMetadataJson { get; private set; }

    private ToolInvocationEvent() { }

    public static ToolInvocationEvent Create(
        Guid invocationRecordId, string eventType,
        CallerType callerType, string? governanceMetadataJson,
        long? durationMs, IDateTimeProvider clock) =>
        new()
        {
            Id                    = Guid.NewGuid(),
            InvocationRecordId    = invocationRecordId,
            EventType             = eventType,
            OccurredAt            = clock.UtcNow,
            DurationMs            = durationMs,
            CallerType            = callerType,
            GovernanceMetadataJson = governanceMetadataJson,
            CreatedAt             = clock.UtcNow,
            UpdatedAt             = clock.UtcNow,
        };

    // NO Update/Modify methods — Create() factory only (H1).
}
