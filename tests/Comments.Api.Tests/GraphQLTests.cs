using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Comments.Api.Tests;

public sealed class GraphQLTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _http = factory.CreateClient();
    private CommentsClient Api => new(_http);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task Query_returns_sorted_pages_and_threads()
    {
        var root = await Api.PostOkAsync("GraphRoot", "graph@example.com", "graph root");
        var reply = await Api.PostOkAsync("GraphReply", "graph-reply@example.com", "graph <i>reply</i>", parentId: root.Id);

        var data = await QueryAsync(
            """
            query ($id: Long!) {
              comments(page: 1, sort: CREATED_AT, direction: DESC) { pageSize totalCount totalPages items { id userName replyCount } }
              thread(rootId: $id) { items { id parentId depth html } nextAfter }
              comment(id: $id) { userName email }
            }
            """,
            new { id = root.Id });

        var comments = data.GetProperty("comments");
        Assert.Equal(25, comments.GetProperty("pageSize").GetInt32());
        var first = comments.GetProperty("items")[0];
        Assert.Equal((root.Id, 1), (first.GetProperty("id").GetInt64(), first.GetProperty("replyCount").GetInt32()));

        var item = Assert.Single(data.GetProperty("thread").GetProperty("items").EnumerateArray());
        Assert.Equal((reply.Id, root.Id, 1, "graph <i>reply</i>"), (
            item.GetProperty("id").GetInt64(),
            item.GetProperty("parentId").GetInt64(),
            item.GetProperty("depth").GetInt32(),
            item.GetProperty("html").GetString()));

        Assert.Equal("GraphRoot", data.GetProperty("comment").GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Search_is_available_over_graphql_and_rest()
    {
        await factory.ConsumersReadyAsync();
        var posted = await Api.PostOkAsync("Searcher", "searcher@example.com", "quokkas smile all day");

        JsonElement hits = default;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var data = await QueryAsync("""query { search(query: "quokka", page: 1) { total hits { id userName snippet } } }""");
            hits = data.GetProperty("search").GetProperty("hits");
            if (hits.GetArrayLength() > 0)
            {
                break;
            }

            await Task.Delay(300, Ct);
        }

        var hit = Assert.Single(hits.EnumerateArray());
        Assert.Equal(posted.Id, hit.GetProperty("id").GetInt64());
        Assert.Contains("<mark>quokkas</mark>", hit.GetProperty("snippet").GetString(), StringComparison.Ordinal);

        var rest = await Api.GetAsync<JsonElement>("/api/search?q=quokka");
        Assert.Equal(posted.Id, rest.GetProperty("hits")[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Invalid_query_returns_errors_not_data()
    {
        using var response = await _http.PostAsJsonAsync("/graphql", new { query = "{ comments { nope } }" }, Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(body.TryGetProperty("errors", out _));
    }

    private async Task<JsonElement> QueryAsync(string query, object? variables = null)
    {
        using var response = await _http.PostAsJsonAsync("/graphql", new { query, variables }, Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(body.TryGetProperty("errors", out var errors), errors.ToString());
        return body.GetProperty("data");
    }
}
