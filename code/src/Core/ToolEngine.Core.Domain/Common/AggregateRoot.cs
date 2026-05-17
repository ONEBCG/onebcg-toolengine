namespace ToolEngine.Core.Domain.Common;

public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    private readonly List<DomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id) { }

    // For EF Core materialization only.
    protected AggregateRoot() { }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(DomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
