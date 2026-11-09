using Comments.Application;
using Comments.Application.Abstractions;
using Comments.Infrastructure.Caching;
using Comments.Infrastructure.Captcha;
using Comments.Infrastructure.Files;
using Comments.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Comments.Infrastructure;

/// <summary>
/// Registers the application and its backing services. Connection strings come from <c>ConnectionStrings:{name}</c>,
/// which the Aspire AppHost, Docker Compose or the Azure deployment provide.
/// </summary>
public static class DependencyInjection
{
    public const string SqlDatabase = "commentsdb";
    public const string Redis = "redis";
    public const string RabbitMq = "rabbitmq";
    public const string Elasticsearch = "elasticsearch";
    public const string Blobs = "blobs";

    public static IHostApplicationBuilder AddCommentsInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddSqlServerDbContext<CommentsDbContext>(SqlDatabase);
        builder.Services.AddScoped<ICommentsDbContext>(sp => sp.GetRequiredService<CommentsDbContext>());

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
}
