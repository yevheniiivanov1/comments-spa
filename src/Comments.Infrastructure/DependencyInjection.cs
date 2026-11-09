using Comments.Application;
using Comments.Application.Abstractions;
using Comments.Infrastructure.Caching;
using Comments.Infrastructure.Captcha;
using Comments.Infrastructure.Files;
using Comments.Infrastructure.Messaging;
using Comments.Infrastructure.Persistence;
using Comments.Infrastructure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Comments.Infrastructure;

/// <summary>
/// Registers backing services. Connection strings come from <c>ConnectionStrings:{name}</c>, which the Aspire AppHost,
/// Docker Compose or the Azure deployment provide. The API and the worker each take only the parts they use.
/// </summary>
public static class DependencyInjection
{
    public const string SqlDatabase = "commentsdb";
    public const string Redis = "redis";
    public const string RabbitMq = "rabbitmq";
    public const string Elasticsearch = "elasticsearch";
    public const string Blobs = "blobs";

    /// <summary>Everything the API needs: database, cache, CAPTCHA, files, broker, search and the use cases.</summary>
    public static IHostApplicationBuilder AddCommentsApi(this IHostApplicationBuilder builder)
    {
        builder.AddCommentsPersistence();
        builder.AddCommentsMessaging();
        builder.AddCommentsSearch();

        builder.AddRedisClient(Redis);
        builder.Services.AddSingleton<ICommentCache, RedisCommentCache>();

        builder.Services.AddOptions<CaptchaOptions>().BindConfiguration(CaptchaOptions.Section);
        builder.Services.AddSingleton<CaptchaRenderer>();
        builder.Services.AddSingleton<ICaptchaService, CaptchaService>();

        builder.AddAzureBlobServiceClient(Blobs);
        builder.Services.AddSingleton<IFileStorage, BlobFileStorage>();
        builder.Services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();

        builder.Services.AddApplication();
        return builder;
    }

    /// <summary>The background side: publishes the outbox to RabbitMQ and keeps the search index up to date.</summary>
    public static IHostApplicationBuilder AddCommentsWorker(this IHostApplicationBuilder builder)
    {
        builder.AddCommentsPersistence();
        builder.AddCommentsMessaging();
        builder.AddCommentsSearch();
        builder.Services.AddCommentsBackgroundServices();
        return builder;
    }

    /// <summary>The worker's hosted services alone, for hosts that already registered the clients (the integration tests).</summary>
    public static IServiceCollection AddCommentsBackgroundServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<SearchIndexingOptions>().BindConfiguration(SearchIndexingOptions.Section);
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.Section);
        services.AddHostedService<OutboxDispatcher>();
        services.AddSingleton<OutboxCleaner>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxCleaner>());
        services.AddHostedService<SearchIndexerConsumer>();
        services.AddScoped<SearchReindexer>();
        return services;
    }

    public static IHostApplicationBuilder AddCommentsPersistence(this IHostApplicationBuilder builder)
    {
        builder.AddSqlServerDbContext<CommentsDbContext>(SqlDatabase);
        builder.Services.AddScoped<ICommentsDbContext>(sp => sp.GetRequiredService<CommentsDbContext>());
        return builder;
    }

    public static IHostApplicationBuilder AddCommentsMessaging(this IHostApplicationBuilder builder)
    {
        builder.AddRabbitMQClient(RabbitMq);
        return builder;
    }

    public static IHostApplicationBuilder AddCommentsSearch(this IHostApplicationBuilder builder)
    {
        // A hung node should fail a request in seconds (the indexer then schedules a retry), not after the client's default minute.
        var timeout = builder.Configuration.GetValue("Search:RequestTimeout", TimeSpan.FromSeconds(10));
        builder.AddElasticsearchClient(Elasticsearch, configureClientSettings: settings => settings.RequestTimeout(timeout));
        builder.Services.AddSingleton<ElasticCommentSearch>();
        builder.Services.AddSingleton<ICommentSearch>(sp => sp.GetRequiredService<ElasticCommentSearch>());
        builder.Services.AddSingleton<ICommentIndexer>(sp => sp.GetRequiredService<ElasticCommentSearch>());
        return builder;
    }
}
