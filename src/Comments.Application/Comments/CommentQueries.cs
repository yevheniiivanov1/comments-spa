using System.Globalization;
using Comments.Application.Abstractions;
using Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;

namespace Comments.Application.Comments;

/// <summary>
/// Read side: pages of top-level comments and threads of replies, both behind the cache.
/// <para>
/// A page is read in two steps: ids first, from the narrow filtered index of the chosen sort (OFFSET skips index entries only),
/// then the 25 full rows by primary key. Reading full rows with OFFSET directly would drag every skipped row's text along.
/// </para>
/// </summary>
public sealed class CommentQueries(ICommentsDbContext db, ICommentCache cache)
{
    public const int PageSize = 25;
    public const int ThreadPageSize = 200;
    public const int MaxPage = 100_000;

    /// <summary>
    /// The number of threads, kept as a counter instead of a cached COUNT: a COUNT over ~300k index entries costs ~75 ms of
    /// CPU, and with a cached value it ran again after every new thread — the top SQL cost in the write-heavy load test.
    /// </summary>
    public const string TopLevelCounter = "top-level";

    public Task<CommentPage> GetPageAsync(int page, CommentSort sort, SortDirection direction, CancellationToken cancellationToken)
    {
        page = Math.Clamp(page, 1, MaxPage);
        var key = string.Create(CultureInfo.InvariantCulture, $"page:{sort}:{direction}:{page}");
        return cache.GetOrCreateAsync(CacheRegion.TopLevel, key, ct => LoadPageAsync(page, sort, direction, ct), cancellationToken);
    }

    public Task<ThreadPage> GetThreadAsync(long rootId, long after, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"after:{after}");
        return cache.GetOrCreateAsync(CacheRegion.Thread(rootId), key, ct => LoadThreadAsync(rootId, after, ct), cancellationToken);
    }

    public async Task<CommentDto?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var comment = await db.Comments.AsNoTracking()
            .Include(c => c.Attachment)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return comment is null ? null : CommentDto.From(comment);
    }

    private async Task<CommentPage> LoadPageAsync(int page, CommentSort sort, SortDirection direction, CancellationToken cancellationToken)
    {
        var total = (int)await cache.GetCounterAsync(
            TopLevelCounter,
            ct => db.Comments.LongCountAsync(c => c.ParentId == null, ct),
            cancellationToken);

        var ids = await Sort(db.Comments.Where(c => c.ParentId == null), sort, direction)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var rows = ids.Count == 0
            ? new Dictionary<long, Comment>()
            : await db.Comments.AsNoTracking()
                .Include(c => c.Attachment)
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        var items = ids.Where(rows.ContainsKey).Select(id => CommentDto.From(rows[id])).ToList();
        return new CommentPage(items, page, PageSize, total, sort, direction);
    }

    private async Task<ThreadPage> LoadThreadAsync(long rootId, long after, CancellationToken cancellationToken)
    {
        var rows = await db.Comments.AsNoTracking()
            .Include(c => c.Attachment)
            .Where(c => c.RootId == rootId && c.Id > after)
            .OrderBy(c => c.Id)
            .Take(ThreadPageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > ThreadPageSize;
        var items = rows.Take(ThreadPageSize).Select(CommentDto.From).ToList();
        return new ThreadPage(rootId, items, hasMore ? items[^1].Id : null);
    }

    /// <summary>Each sort has a matching filtered index (key, Id) on top-level comments; Id breaks ties so pages are stable.</summary>
    private static IQueryable<Comment> Sort(IQueryable<Comment> query, CommentSort sort, SortDirection direction) =>
        (sort, direction) switch
        {
            (CommentSort.UserName, SortDirection.Asc) => query.OrderBy(c => c.UserName).ThenBy(c => c.Id),
            (CommentSort.UserName, SortDirection.Desc) => query.OrderByDescending(c => c.UserName).ThenByDescending(c => c.Id),
            (CommentSort.Email, SortDirection.Asc) => query.OrderBy(c => c.Email).ThenBy(c => c.Id),
            (CommentSort.Email, SortDirection.Desc) => query.OrderByDescending(c => c.Email).ThenByDescending(c => c.Id),
            (_, SortDirection.Asc) => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            _ => query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id),
        };
}
