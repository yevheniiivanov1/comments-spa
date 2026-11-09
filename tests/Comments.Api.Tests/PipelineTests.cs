using System.Text;
using System.Text.Json;
using Comments.Application.Abstractions;
using Comments.Application.Comments;
using Comments.Infrastructure.Json;
using Comments.Infrastructure.Messaging;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Comments.Api.Tests;

/// <summary>A posted comment travels outbox → RabbitMQ → live WebSocket clients and the Elasticsearch index.</summary>
public sealed class PipelineTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _http = factory.CreateClient();
    private CommentsClient Api => new(_http);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task Posted_comment_reaches_live_clients_and_the_search_index()
    {
        await factory.ConsumersReadyAsync();
        var received = new TaskCompletionSource<CommentDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/comments"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(options => JsonDefaults.Configure(options.PayloadSerializerOptions))
            .Build();
        hub.On<CommentDto>("commentPosted", comment =>
        {
            if (comment.UserName == "Pipeline")
            {
                received.TrySetResult(comment);
            }
        });
        await hub.StartAsync(Ct);

        var posted = await Api.PostOkAsync("Pipeline", "pipeline@example.com", "a zebracorn <code>if (a &lt; b)</code> grazes");

        var live = await received.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal(posted, live);

        var hit = await SearchUntilFoundAsync("zebracorns");
        Assert.Equal(posted.Id, hit.Id);
        Assert.Contains("<mark>zebracorn</mark>", hit.Snippet, StringComparison.Ordinal);
        Assert.Contains("a &lt; b", hit.Snippet, StringComparison.Ordinal);

        var processed = await factory.WithDbAsync(db => db.OutboxMessages
            .Where(m => m.Payload.Contains($"\"id\":{posted.Id},"))
            .Select(m => m.ProcessedAt)
            .SingleAsync(Ct));
        Assert.NotNull(processed);
    }

    [Fact]
    public async Task Search_matches_other_word_forms()
    {
        await factory.ConsumersReadyAsync();
        var posted = await Api.PostOkAsync("Stemming", "stemming@example.com", "Careful studies of competitors");

        var hit = await SearchUntilFoundAsync("studied competitor");

        Assert.Equal(posted.Id, hit.Id);
    }

    [Fact]
    public async Task Comment_posted_while_Elasticsearch_is_down_is_indexed_once_it_is_back()
    {
        await factory.ConsumersReadyAsync();
        CommentDto posted;

        await using (await factory.PauseElasticsearchAsync())
        {
            posted = await Api.PostOkAsync("Outage", "outage@example.com", "glyptodons survived the outage");

            // Long enough for several failed attempts (2 s request timeout, 1 s retry delay in tests).
            await Task.Delay(TimeSpan.FromSeconds(8), Ct);
        }

        var hit = await SearchUntilFoundAsync("glyptodons");
        Assert.Equal(posted.Id, hit.Id);
        Assert.Equal(0u, await DeadLetterCountAsync());
    }

    [Fact]
    public async Task Replay_returns_dead_lettered_events_to_their_queue()
    {
        await factory.ConsumersReadyAsync();
        var comment = new CommentDto(990_000_001, null, null, 0, "Replayed", "replayed@example.com", null, "a lost megatherium", DateTimeOffset.UtcNow, 0, null);
        await DeadLetterAsync(new CommentPostedMessage(comment));

        var replayed = await ActivatorUtilities.CreateInstance<DeadLetterReplayer>(factory.Services).RunAsync(Ct);

        Assert.Equal(1, replayed);
        Assert.Equal(comment.Id, (await SearchUntilFoundAsync("megatherium")).Id);
    }

    /// <summary>Publishes a message to the dead-letter exchange as the broker would after the indexer gave up on it.</summary>
    private async Task DeadLetterAsync(CommentPostedMessage message)
    {
        var connection = factory.Services.GetRequiredService<IConnection>();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var death = new Dictionary<string, object?> { ["queue"] = Encoding.UTF8.GetBytes(RabbitMqTopology.SearchIndexerQueue), ["reason"] = Encoding.UTF8.GetBytes("rejected") };
        var properties = new BasicProperties { Headers = new Dictionary<string, object?> { ["x-death"] = new List<object> { death } } };
        await channel.BasicPublishAsync(RabbitMqTopology.DeadLetterExchange, CommentPostedMessage.RoutingKey, false, properties,
            JsonSerializer.SerializeToUtf8Bytes(message, JsonDefaults.Options), Ct);
    }

    private async Task<uint> DeadLetterCountAsync()
    {
        var connection = factory.Services.GetRequiredService<IConnection>();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        return (await channel.QueueDeclarePassiveAsync(RabbitMqTopology.DeadLetterQueue, Ct)).MessageCount;
    }

    private async Task<SearchHit> SearchUntilFoundAsync(string query)
    {
        var search = factory.Services.GetRequiredService<ICommentSearch>();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var result = await search.SearchAsync(query, 1, Ct);
            if (result.Hits.Count > 0)
            {
                return Assert.Single(result.Hits);
            }

            Assert.True(DateTime.UtcNow < deadline, $"'{query}' was not indexed in time.");
            await Task.Delay(300, Ct);
        }
    }
}
