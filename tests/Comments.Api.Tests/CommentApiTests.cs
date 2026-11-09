using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Comments.Application.Comments;
using Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Comments.Api.Tests;

public sealed class CommentApiTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _http = factory.CreateClient();
    private CommentsClient Api => new(_http);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task Posted_comment_comes_first_in_the_default_order()
    {
        var posted = await Api.PostOkAsync("Lifo1", "lifo1@example.com", "first <strong>bold</strong>");
        var later = await Api.PostOkAsync("Lifo2", "lifo2@example.com", "second");

        var page = await Api.GetAsync<CommentPage>("/api/comments");

        Assert.Equal([later.Id, posted.Id], page.Items.Take(2).Select(c => c.Id));
        Assert.Equal("first <strong>bold</strong>", page.Items[1].Html);
        Assert.Equal(CommentQueries.PageSize, page.PageSize);
        Assert.Equal((CommentSort.CreatedAt, SortDirection.Desc), (page.Sort, page.Direction));
    }

    [Fact]
    public async Task Invalid_fields_are_reported_per_field()
    {
        using var response = await Api.PostAsync(
            userName: "John Doe",
            email: "not-an-email",
            homePage: "javascript:alert(1)",
            text: "<script>alert(1)</script>",
            captchaAnswer: "wrong");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>(CommentsClient.Json, Ct);
        Assert.Equal(["captcha", "email", "homePage", "text", "userName"], problem!.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("Tag <script> is not allowed", problem.Errors["text"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Captcha_is_single_use_and_required()
    {
        var captcha = await Api.GetCaptchaAsync();
        Assert.StartsWith("data:image/png;base64,", captcha.Image, StringComparison.Ordinal);

        using var first = await Api.PostAsync(captcha: captcha);
        using var replay = await Api.PostAsync(captcha: captcha);
        using var wrong = await Api.PostAsync(captchaAnswer: "AAAAA");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var problem = await replay.Content.ReadFromJsonAsync<ProblemDto>(CommentsClient.Json, Ct);
        Assert.Equal(["captcha"], problem!.Errors.Keys);
    }

    [Fact]
    public async Task Captcha_answer_is_case_insensitive()
    {
        var captcha = await Api.GetCaptchaAsync();

        using var response = await Api.PostAsync(captcha: captcha, captchaAnswer: captcha.Answer!.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Replies_form_a_thread_and_update_the_cached_list()
    {
        var root = await Api.PostOkAsync("ThreadRoot", "root@example.com", "root");
        var before = await Api.GetAsync<CommentPage>("/api/comments");
        Assert.Equal(0, before.Items.Single(c => c.Id == root.Id).ReplyCount);

        var reply = await Api.PostOkAsync("Replier", "replier@example.com", "reply", parentId: root.Id);
        var nested = await Api.PostOkAsync("Nested", "nested@example.com", "nested", parentId: reply.Id);

        var thread = await Api.GetAsync<ThreadPage>($"/api/comments/{root.Id}/thread");
        Assert.Equal([reply.Id, nested.Id], thread.Items.Select(c => c.Id));
        Assert.Equal((root.Id, root.Id, 1), (thread.Items[0].ParentId, thread.Items[0].RootId, thread.Items[0].Depth));
        Assert.Equal((reply.Id, root.Id, 2), (thread.Items[1].ParentId, thread.Items[1].RootId, thread.Items[1].Depth));
        Assert.Null(thread.NextAfter);

        var after = await Api.GetAsync<CommentPage>("/api/comments");
        Assert.Equal(2, after.Items.Single(c => c.Id == root.Id).ReplyCount);
        Assert.DoesNotContain(after.Items, c => c.Id == reply.Id);
    }

    [Fact]
    public async Task Reply_to_a_missing_comment_is_rejected()
    {
        using var response = await Api.PostAsync(parentId: 999_999_999);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDto>(CommentsClient.Json, Ct);
        Assert.Equal(["parentId"], problem!.Errors.Keys);
    }

    [Fact]
    public async Task Top_level_list_sorts_by_user_name_and_email_both_ways_and_pages_by_25()
    {
        for (var i = 0; i < 30; i++)
        {
            await Api.PostOkAsync($"Sort{i:D2}", $"z{29 - i:D2}@sort.example.com", $"sort {i}");
        }

        var all = new List<CommentDto>();
        var first = await Api.GetAsync<CommentPage>("/api/comments?sort=userName&direction=asc&page=1");
        for (var page = 1; page <= first.TotalPages; page++)
        {
            all.AddRange((await Api.GetAsync<CommentPage>($"/api/comments?sort=userName&direction=asc&page={page}")).Items);
        }

        Assert.Equal(25, first.Items.Count);
        Assert.Equal(first.TotalCount, all.Count);
        Assert.Equal(all.Select(c => c.UserName).Order(StringComparer.OrdinalIgnoreCase), all.Select(c => c.UserName));

        var byEmailDesc = await Api.GetAsync<CommentPage>("/api/comments?sort=email&direction=desc");
        var emails = byEmailDesc.Items.Select(c => c.Email).ToList();
        Assert.Equal(emails.OrderDescending(StringComparer.OrdinalIgnoreCase), emails);

        var byNameDesc = await Api.GetAsync<CommentPage>("/api/comments?sort=userName&direction=desc");
        Assert.Equal(byNameDesc.Items.Select(c => c.UserName).OrderDescending(StringComparer.OrdinalIgnoreCase), byNameDesc.Items.Select(c => c.UserName));

        var oldestFirst = await Api.GetAsync<CommentPage>("/api/comments?sort=createdAt&direction=asc");
        Assert.Equal(oldestFirst.Items.Select(c => c.Id).Order(), oldestFirst.Items.Select(c => c.Id));

        using var invalid = await _http.GetAsync("/api/comments?sort=Id;DROP TABLE Comments", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData(800, 600, 320, 240)]
    [InlineData(1000, 200, 320, 64)]
    [InlineData(300, 900, 80, 240)]
    [InlineData(200, 100, 200, 100)]
    public async Task Images_are_scaled_down_proportionally_to_fit_320x240(int width, int height, int expectedWidth, int expectedHeight)
    {
        var comment = await Api.PostOkAsync(text: "with image", file: ("photo.png", Image(width, height, SKEncodedImageFormat.Png), "image/png"));

        var attachment = comment.Attachment!;
        Assert.Equal((AttachmentKind.Image, expectedWidth, expectedHeight), (attachment.Kind, attachment.Width, attachment.Height));

        using var file = await _http.GetAsync(attachment.Url, Ct);
        Assert.Equal("image/png", file.Content.Headers.ContentType!.MediaType);
        using var bitmap = SKBitmap.Decode(await file.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal((expectedWidth, expectedHeight), (bitmap.Width, bitmap.Height));
    }

    [Fact]
    public async Task Jpeg_stays_jpeg()
    {
        var comment = await Api.PostOkAsync(file: ("photo.JPG", Image(640, 480, SKEncodedImageFormat.Jpeg), "image/jpeg"));

        Assert.Equal(("image/jpeg", 320, 240), (comment.Attachment!.ContentType, comment.Attachment.Width, comment.Attachment.Height));
        Assert.EndsWith(".jpg", comment.Attachment.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_are_checked_by_content_and_size()
    {
        using var fake = await Api.PostAsync(file: ("evil.png", "<script>alert(1)</script>"u8.ToArray(), "image/png"));
        using var exe = await Api.PostAsync(file: ("tool.exe", [0x4D, 0x5A], "application/octet-stream"));
        using var big = await Api.PostAsync(file: ("big.txt", Encoding.UTF8.GetBytes(new string('a', 100 * 1024 + 1)), "text/plain"));

        foreach (var response in new[] { fake, exe, big })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDto>(CommentsClient.Json, Ct);
            Assert.Equal(["file"], problem!.Errors.Keys);
        }
    }

    [Fact]
    public async Task Text_file_is_stored_and_served_sandboxed()
    {
        var text = "Hello, <script>alert(1)</script>";
        var comment = await Api.PostOkAsync(file: ("notes.txt", Encoding.UTF8.GetBytes(text), "text/plain"));

        Assert.Equal(AttachmentKind.Text, comment.Attachment!.Kind);
        using var file = await _http.GetAsync(comment.Attachment.Url, Ct);
        Assert.Equal("text/plain; charset=utf-8", file.Content.Headers.ContentType!.ToString());
        Assert.Contains("sandbox", file.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(text, await file.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Windows_1251_text_is_labelled_as_such()
    {
        var bytes = new byte[] { 0x43, 0x61, 0x66, 0xE9 };
        var comment = await Api.PostOkAsync(file: ("cp1251.txt", bytes, "text/plain"));

        Assert.Equal("text/plain; charset=windows-1251", comment.Attachment!.ContentType);
    }

    [Fact]
    public async Task Comment_writes_user_client_info_and_outbox_message()
    {
        var comment = await Api.PostOkAsync("Outboxer", "outbox@example.com", "to the outbox");

        var (user, ip, message) = await factory.WithDbAsync(async db =>
        {
            var row = await db.Comments.Include(c => c.User).SingleAsync(c => c.Id == comment.Id, Ct);
            var outbox = await db.OutboxMessages.Where(m => m.Payload.Contains($"\"id\":{comment.Id},")).SingleAsync(Ct);
            return (row.User, row.Client.IpAddress, outbox);
        });

        Assert.Equal(("Outboxer", "outbox@example.com"), (user.UserName, user.Email));
        Assert.False(string.IsNullOrEmpty(ip));
        Assert.Equal(CommentPostedMessage.RoutingKey, message.Type);
        var payload = JsonSerializer.Deserialize<CommentPostedMessage>(message.Payload, CommentsClient.Json)!;
        Assert.Equal(comment, payload.Comment);
    }

    [Fact]
    public async Task Same_email_reuses_the_user_and_keeps_each_comments_name()
    {
        var first = await Api.PostOkAsync("OldName", "same@example.com", "one");
        var second = await Api.PostOkAsync("NewName", "SAME@example.com", "two");

        var (users, names) = await factory.WithDbAsync(async db => (
            await db.Users.CountAsync(u => u.Email == "same@example.com", Ct),
            await db.Comments.Where(c => c.Id == first.Id || c.Id == second.Id).OrderBy(c => c.Id).Select(c => c.UserName).ToListAsync(Ct)));

        Assert.Equal(1, users);
        Assert.Equal(["OldName", "NewName"], names);
    }

    [Fact]
    public async Task Preview_returns_the_xhtml_or_the_errors()
    {
        var ok = await (await _http.PostAsJsonAsync("/api/comments/preview", new { text = "<i>x</i> & y" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var bad = await (await _http.PostAsJsonAsync("/api/comments/preview", new { text = "<i>x" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal("<i>x</i> &amp; y", ok.GetProperty("html").GetString());
        Assert.Equal("Tag <i> is not closed.", bad.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        using var response = await _http.GetAsync("/api/comments", Ct);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
        }

        using var stream = new MemoryStream();
        bitmap.Encode(stream, format, 90);
        return stream.ToArray();
    }
}
