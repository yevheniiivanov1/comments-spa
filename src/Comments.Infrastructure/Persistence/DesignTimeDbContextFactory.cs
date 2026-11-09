using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Comments.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without starting a host or a database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CommentsDbContext>
{
    public CommentsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CommentsDbContext>()
            .UseSqlServer("Server=localhost;Database=comments;Integrated Security=true;TrustServerCertificate=true")
            .Options);
}
