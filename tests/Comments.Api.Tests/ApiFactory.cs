using Comments.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Comments.Api.Tests.ApiFactory))]
// Tests share one database and assert on the order of the top-level list, so they run one at a time.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Comments.Api.Tests;

/// <summary>The real API against throwaway SQL Server, Redis and Azurite containers.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(_sql.StartAsync(), _redis.StartAsync(), _azurite.StartAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var sql = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "comments" };

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:commentsdb", sql.ConnectionString);
        builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
        builder.UseSetting("ConnectionStrings:blobs", _azurite.GetConnectionString());
        builder.UseSetting("Captcha:ExposeAnswerForTesting", "true");
        builder.UseSetting("RateLimits:PostsPerMinute", "100000");
        builder.UseSetting("RateLimits:CaptchasPerMinute", "100000");
    }

    public async Task<T> WithDbAsync<T>(Func<CommentsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CommentsDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _azurite.DisposeAsync().AsTask());
    }
}
