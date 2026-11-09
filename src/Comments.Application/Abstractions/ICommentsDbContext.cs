using Comments.Domain.Comments;
using Comments.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Comments.Application.Abstractions;

/// <summary>The relational store as the use cases see it. Saving entities that raised domain events also writes them to the outbox.</summary>
public interface ICommentsDbContext
{
    DbSet<Comment> Comments { get; }

    DbSet<User> Users { get; }

    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    bool IsUniqueConstraintViolation(DbUpdateException exception);
}
