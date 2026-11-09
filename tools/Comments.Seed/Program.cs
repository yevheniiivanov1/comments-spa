using System.Diagnostics;
using Comments.Infrastructure.Persistence;
using Comments.Seed;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

// Bulk-loads users and comments for load testing: dotnet run -- --comments 1000000 --users 100000
// Connection strings come from --sql/--redis or the same ConnectionStrings__* variables the API uses.
var options = SeedOptions.Parse(args);
var watch = Stopwatch.StartNew();

await using (var db = new CommentsDbContext(new DbContextOptionsBuilder<CommentsDbContext>()
    .UseSqlServer(options.SqlConnectionString, sql => sql.CommandTimeout(600))
    .Options))
{
    Console.WriteLine("Applying migrations…");
    await db.Database.MigrateAsync();
}

var seeder = new Seeder(options);
var (users, comments) = await seeder.RunAsync();
Console.WriteLine($"Inserted {users:N0} users and {comments:N0} comments in {watch.Elapsed:mm\\:ss}.");

if (options.PopularThreadsFile is { } file)
{
    await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(seeder.PopularThreadIds));
    Console.WriteLine($"Popular thread ids written to {file}.");
}

if (options.RedisConnectionString is { } redis)
{
    await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
    await connection.GetDatabase().StringIncrementAsync("comments:top:version");
    Console.WriteLine("Invalidated the cached top-level pages.");
}

Console.WriteLine("Next: rebuild the search index with `docker compose run --rm worker reindex`.");
