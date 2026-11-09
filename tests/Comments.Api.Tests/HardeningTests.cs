using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Comments.Api.Tests;

public sealed class HardeningTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _http = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Theory]
    [InlineData("/api/comments/abc")]
    [InlineData("/api/nothing-here")]
    [InlineData("/files/images/not-a-key.png")]
    [InlineData("/hubs/unknown")]
    public async Task Unknown_api_paths_are_404_not_the_spa(string path)
    {
        using var response = await _http.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GraphQL_rejects_a_query_that_packs_many_searches_into_aliases()
    {
        var aliases = string.Join(' ', Enumerable.Range(1, 6).Select(i => $"s{i}: search(query: \"a{i}\") {{ total }}"));

        using var response = await _http.PostAsJsonAsync("/graphql", new { query = $"{{ {aliases} }}" }, Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.True(body.TryGetProperty("errors", out var errors), body.ToString());
        Assert.Contains("cost", errors.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task Rate_limited_requests_get_429()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder
            .UseSetting("RateLimits:PreviewsPerMinute", "2")
            .UseSetting("RateLimits:GraphQLRequestsPerMinute", "2"));
        using var http = limited.CreateClient();

        var previews = new List<HttpStatusCode>();
        var graphql = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var preview = await http.PostAsJsonAsync("/api/comments/preview", new { text = "hi" }, Ct);
            using var query = await http.PostAsJsonAsync("/graphql", new { query = "{ comments { totalCount } }" }, Ct);
            previews.Add(preview.StatusCode);
            graphql.Add(query.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], previews);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], graphql);
    }

    /// <summary>TestServer does not enforce body size limits (Kestrel does), so the test checks the endpoint's declared limit.</summary>
    [Theory]
    [InlineData("/api/comments/preview", 64 * 1024)]
    [InlineData("/api/comments/", 6 * 1024 * 1024)]
    public void Posting_endpoints_declare_a_request_size_limit(string route, long limit)
    {
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == route && e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true);

        Assert.Equal(limit, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
    }
}
