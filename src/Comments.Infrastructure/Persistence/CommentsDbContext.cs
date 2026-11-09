using Comments.Application.Abstractions;
using Comments.Domain.Comments;
using Comments.Domain.Common;
using Comments.Domain.Users;
using Comments.Infrastructure.Persistence.Outbox;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Comments.Infrastructure.Persistence;

/// <summary>
/// EF Core context over SQL Server. Saving entities that raised domain events writes them to <see cref="OutboxMessages"/>
/// in the caller's transaction: the entities are inserted first (so identity ids exist), then the outbox rows.
/// </summary>
public sealed class CommentsDbContext(DbContextOptions<CommentsDbContext> options) : DbContext(options), ICommentsDbContext
{
    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var raising = ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        if (raising.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Entities with domain events must be saved in a transaction, so the outbox is written atomically.");
        }

        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        foreach (var entity in raising)
        {
            OutboxMessages.AddRange(entity.DomainEvents.Select(OutboxMessage.From));
            entity.ClearDomainEvents();
        }

        return written + await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommentsDbContext).Assembly);
}
