namespace Comments.Application.Abstractions;

/// <summary>
/// Read-through cache for query results, split into regions that are invalidated as a whole
/// (all pages of the top-level list, or one thread). Implementations must fall back to the factory when the cache is down,
/// and <see cref="InvalidateAsync"/> must not throw: it runs after a commit, when the write has already succeeded.
/// </summary>
public interface ICommentCache
{
    Task<T> GetOrCreateAsync<T>(CacheRegion region, string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken);

    Task InvalidateAsync(CacheRegion region, CancellationToken cancellationToken);
}

/// <summary>
/// A group of cache entries invalidated together, and how long its entries live.
/// <para>
/// The top-level list is invalidated only when a new thread starts. Replies change the reply counts on its rows, but
/// flushing every page on every reply would make the list cache useless at the expected write rate; the counts may lag
/// by up to <see cref="TopLevelLifetime"/>, while open pages get them live over WebSocket. A thread is invalidated by
/// each reply to it.
/// </para>
/// </summary>
public readonly record struct CacheRegion(string Name, TimeSpan Lifetime)
{
    public static readonly TimeSpan TopLevelLifetime = TimeSpan.FromSeconds(30);

    public static CacheRegion TopLevel { get; } = new("top", TopLevelLifetime);

    public static CacheRegion Thread(long rootId) => new($"thread:{rootId}", TimeSpan.FromMinutes(10));
}
