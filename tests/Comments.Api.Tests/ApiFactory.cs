using Comments.Infrastructure;
using Comments.Infrastructure.Messaging;
using Comments.Infrastructure.Persistence;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Comments.Api.Tests.ApiFactory))]
// Tests share one database and assert on the order of the top-level list, so they run one at a time.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Comments.Api.Tests;

/// <summary>
/// The real API against throwaway SQL Server, Redis, RabbitMQ, Elasticsearch and Azurite containers.
/// The worker's background services run in the same host, so a test can follow a comment through the outbox,
/// the broker, the search index and the WebSocket hub.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.1-management").Build();
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();
    private readonly IContainer _elastic = new ContainerBuilder("elasticsearch:8.19.4")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(9200, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9200).ForPath("/_cluster/health")))
        .Build();

    public async ValueTask InitializeAsync() => await Task.WhenAll(
        _sql.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync(), _azurite.StartAsync(), _elastic.StartAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var sql = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "comments" };

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:commentsdb", sql.ConnectionString);
        // Short timeouts so tests that pause Redis see failures in about a second, as a production client would after its own timeout.
        builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString() + ",asyncTimeout=1000,syncTimeout=1000,abortConnect=false");
        builder.UseSetting("ConnectionStrings:rabbitmq", _rabbit.GetConnectionString());
        builder.UseSetting("ConnectionStrings:elasticsearch", $"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(9200)}");
        builder.UseSetting("ConnectionStrings:blobs", _azurite.GetConnectionString());
        builder.UseSetting("Captcha:ExposeAnswerForTesting", "true");
        builder.UseSetting("Search:RequestTimeout", "00:00:02");
        builder.UseSetting("SearchIndexing:RetryDelay", "00:00:01");
        builder.UseSetting("RateLimits:PostsPerMinute", "100000");
        builder.UseSetting("RateLimits:CaptchasPerMinute", "100000");
        builder.ConfigureTestServices(services => services.AddCommentsBackgroundServices());
    }

    /// <summary>Waits until every broker consumer in the host is subscribed, so no event published by a test is missed.</summary>
    public Task ConsumersReadyAsync() =>
        Task.WhenAll(Services.GetServices<IHostedService>()
            .Select(s => s.GetType().BaseType is { IsGenericType: true } b && b.GetGenericTypeDefinition() == typeof(RabbitMqConsumer<>)
                ? (Task)b.GetProperty(nameof(RabbitMqConsumer<object>.Consuming))!.GetValue(s)!
                : Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(60));

    /// <summary>Pauses the Redis container (requests hang and time out, like a stalled server) until the returned scope ends.</summary>
    public async Task<IAsyncDisposable> PauseRedisAsync()
    {
        await _redis.PauseAsync();
        return new Resume(() => _redis.UnpauseAsync());
    }

    /// <summary>Pauses the Elasticsearch container until the returned scope ends.</summary>
    public async Task<IAsyncDisposable> PauseElasticsearchAsync()
    {
        await _elastic.PauseAsync();
        return new Resume(() => _elastic.UnpauseAsync());
    }

    public async Task<T> WithDbAsync<T>(Func<CommentsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CommentsDbContext>());
    }

    private sealed class Resume(Func<Task> resume) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await resume();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(
            _sql.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask(),
            _azurite.DisposeAsync().AsTask(),
            _elastic.DisposeAsync().AsTask());
    }
}
