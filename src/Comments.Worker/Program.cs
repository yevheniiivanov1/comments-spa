using Comments.Infrastructure;
using Comments.Infrastructure.Messaging;
using Comments.Infrastructure.Search;

// The background side of the system: publishes the outbox to RabbitMQ and indexes comments in Elasticsearch.
// One-off commands: "reindex" rebuilds the search index from SQL Server; "replay" returns dead-lettered events to their queues.
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

if (args.Contains("replay", StringComparer.OrdinalIgnoreCase))
{
    builder.AddCommentsMessaging();
    builder.Services.AddSingleton<DeadLetterReplayer>();

    using var host = builder.Build();
    var replayed = await host.Services.GetRequiredService<DeadLetterReplayer>().RunAsync(CancellationToken.None);
    Console.WriteLine($"Replayed {replayed} dead-lettered messages.");
    return;
}

builder.AddCommentsWorker();
await builder.Build().RunAsync();
