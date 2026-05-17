namespace ToolEngine.Core.Abstractions.Audit;

public interface IAuditableEntity
{
    DateTimeOffset  CreatedAt  { get; }
    string          CreatedBy  { get; }
    DateTimeOffset? UpdatedAt  { get; }
    string?         UpdatedBy  { get; }
}
