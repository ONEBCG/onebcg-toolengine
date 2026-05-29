using ToolEngine.Core.Domain.Common;

namespace ToolEngine.Core.Domain.Entities;

// F7 — reliable notification delivery via transactional outbox pattern
public sealed class OutboxMessage : Entity<Guid>
{
    public string          MessageType { get; private set; } = default!;
    public string          Payload     { get; private set; } = default!;
    public DateTimeOffset? SentAt      { get; private set; }
    public int             RetryCount  { get; private set; }
    public string?         Error       { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(string messageType, string payload, DateTimeOffset now) =>
        new()
        {
            Id          = Guid.NewGuid(),
            MessageType = messageType,
            Payload     = payload,
            CreatedAt   = now,
            UpdatedAt   = now,
        };

    public void MarkSent()
    {
        SentAt    = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string err)
    {
        RetryCount++;
        Error     = err;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
