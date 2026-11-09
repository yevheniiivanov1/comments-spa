using Comments.Application.Abstractions;
using Comments.Application.Comments;
using Comments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Comments.Infrastructure.Search;

/// <summary>
/// Rebuilds the search index from SQL Server in id order, in bulk batches (for bulk-loaded data such as the load-test seed,
/// which bypasses the outbox, or after losing the index). Safe to repeat: documents are keyed by comment id.
/// </summary>
public sealed class SearchReindexer(CommentsDbContext db, ICommentIndexer indexer, ILogger<SearchReindexer> logger)
{
    private const int BatchSize = 5_000;

    public async Task<long> RunAsync(CancellationToken cancellationToken)
    {
        await indexer.EnsureIndexAsync(cancellationToken);
        long after = 0;
        long total = 0;

        while (true)
        {
            var batch = await db.Comments.AsNoTracking()
                .Include(c => c.Attachment)
                .Where(c => c.Id > after)
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            await indexer.IndexAsync(batch.Select(CommentDto.From).ToList(), cancellationToken);
            after = batch[^1].Id;
            total += batch.Count;
            logger.LogInformation("Indexed {Total} comments (up to id {Id})", total, after);
        }

        return total;
    }
}
