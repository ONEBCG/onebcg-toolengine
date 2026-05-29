namespace ToolEngine.Core.Abstractions.Persistence;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items        { get; init; } = [];
    public int              TotalCount   { get; init; }
    public int              PageNumber   { get; init; }
    public int              PageSize     { get; init; }
    public int              TotalPages   => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool             HasNext      => PageNumber < TotalPages;
    public bool             HasPrevious  => PageNumber > 1;
}
