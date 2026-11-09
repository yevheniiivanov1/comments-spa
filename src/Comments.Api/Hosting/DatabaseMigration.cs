using Comments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Comments.Api.Hosting;

internal static class DatabaseMigration
{
    /// <summary>
    /// Applies EF Core migrations before the API takes traffic. EF Core takes an application lock while migrating,
    /// so several instances starting at once are safe; turn it off with <c>Database:MigrateOnStartup=false</c>.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Database:MigrateOnStartup", true))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommentsDbContext>();
        await db.Database.MigrateAsync();
    }
}
