namespace ToolEngine.Core.Abstractions.Audit;

public interface IDomainEventEmitter
{
    IReadOnlyList<object> DomainEvents { get; }
    void ClearDomainEvents();
}
