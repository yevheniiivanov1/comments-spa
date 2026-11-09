using Comments.Infrastructure;
using Comments.Infrastructure.Search;

// The background side of the system: publishes the outbox to RabbitMQ and indexes comments in Elasticsearch.
// Run with "reindex" to rebuild the search index from SQL Server and exit.
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

if (args.Contains("reindex", StringComparer.OrdinalIgnoreCase))
{
    builder.AddCommentsPersistence();
    builder.AddCommentsSearch();
    builder.Services.AddScoped<SearchReindexer>();

    using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    var total = await scope.ServiceProvider.GetRequiredService<SearchReindexer>().RunAsync(CancellationToken.None);
    Console.WriteLine($"Reindexed {total} comments.");
    return;
}

builder.AddCommentsWorker();
await builder.Build().RunAsync();
