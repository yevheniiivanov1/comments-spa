namespace Comments.Application.Abstractions;

/// <summary>
/// Read-through cache for query results, split into regions that are invalidated as a whole
/// (all pages of the top-level list, or one thread). Implementations must fall back to the factory when the cache is down.
/// </summary>
public interface ICommentCache
{
    Task<T> GetOrCreateAsync<T>(CacheRegion region, string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken);

    Task InvalidateAsync(CacheRegion region, CancellationToken cancellationToken);
}

public readonly record struct CacheRegion(string Name)
{
    public static CacheRegion TopLevel { get; } = new("top");

    public static CacheRegion Thread(long rootId) => new($"thread:{rootId}");
}
