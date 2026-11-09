using System.Net;
using System.Net.Http.Json;
using Comments.Application.Abstractions;
using Comments.Application.Comments;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Comments.Api.Tests;

/// <summary>What users see when a dependency is slow or down: the documented fallbacks, not 500s.</summary>
public sealed class FailureTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _http = factory.CreateClient();
    private CommentsClient Api => new(_http);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task List_and_threads_are_read_from_the_database_while_Redis_is_paused()
    {
        var root = await Api.PostOkAsync("RedisDown", "redis-down@example.com", "before the outage");

        await using (await factory.PauseRedisAsync())
        {
            using var page = await _http.GetAsync("/api/comments", Ct);
            using var thread = await _http.GetAsync($"/api/comments/{root.Id}/thread", Ct);

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
            var body = await page.Content.ReadFromJsonAsync<CommentPage>(CommentsClient.Json, Ct);
            Assert.Contains(body!.Items, c => c.Id == root.Id);
        }
    }

    [Fact]
    public async Task Posting_while_Redis_is_paused_reports_the_captcha_as_unavailable()
    {
        var captcha = await Api.GetCaptchaAsync();

        await using (await factory.PauseRedisAsync())
        {
            using var response = await Api.PostAsync(captcha: captcha);
            using var picture = await _http.GetAsync("/api/captcha", Ct);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDto>(CommentsClient.Json, Ct);
            Assert.Equal([CaptchaUnavailableException.UserMessage], problem!.Errors["captcha"]);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, picture.StatusCode);
        }
    }

    [Fact]
    public async Task A_cache_failure_after_the_commit_keeps_the_comment_and_its_file()
    {
        await using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ICommentCache>(sp => new InvalidationFails(ActivatorUtilities.CreateInstance<PassThroughCache>(sp)))));
        using var http = failing.CreateClient();

        var comment = await new CommentsClient(http).PostOkAsync(text: "saved despite the cache", file: ("photo.png", Png(), "image/png"));

        using var file = await http.GetAsync(comment.Attachment!.Url, Ct);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.NotNull(await new CommentsClient(http).GetAsync<CommentDto>($"/api/comments/{comment.Id}"));
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using var stream = new MemoryStream();
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
        return stream.ToArray();
    }

    private sealed class PassThroughCache : ICommentCache
    {
        public Task<T> GetOrCreateAsync<T>(CacheRegion region, string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public Task InvalidateAsync(CacheRegion region, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A cache whose invalidation times out, as StackExchange.Redis does when Redis stalls.</summary>
    private sealed class InvalidationFails(ICommentCache inner) : ICommentCache
    {
        public Task<T> GetOrCreateAsync<T>(CacheRegion region, string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            inner.GetOrCreateAsync(region, key, factory, cancellationToken);

        public Task InvalidateAsync(CacheRegion region, CancellationToken cancellationToken) =>
            throw new TimeoutException("Simulated Redis timeout");
    }
}
