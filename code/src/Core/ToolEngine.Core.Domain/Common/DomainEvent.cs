namespace ToolEngine.Core.Domain.Common;

public abstract record DomainEvent(
    Guid           Id         = default,
    DateTimeOffset OccurredAt = default)
{
    public Guid           Id         { get; init; } = Id == default ? Guid.NewGuid() : Id;
    public DateTimeOffset OccurredAt { get; init; } = OccurredAt == default
                                                           ? DateTimeOffset.UtcNow
                                                           : OccurredAt;
}
