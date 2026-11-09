using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Comments.Application.Comments;
using Comments.Infrastructure.Json;

namespace Comments.Api.Tests;

/// <summary>Calls the API like the Angular client does: get a CAPTCHA, then post the form as multipart.</summary>
internal sealed class CommentsClient(HttpClient http)
{
    public static JsonSerializerOptions Json => JsonDefaults.Options;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient Http => http;

    public async Task<CaptchaDto> GetCaptchaAsync() =>
        (await http.GetFromJsonAsync<CaptchaDto>("/api/captcha", Json, Ct))!;

    public async Task<HttpResponseMessage> PostAsync(
        string userName = "Tester",
        string email = "tester@example.com",
        string? homePage = null,
        string text = "Hello",
        long? parentId = null,
        string? captchaAnswer = null,
        CaptchaDto? captcha = null,
        (string Name, byte[] Content, string ContentType)? file = null)
    {
        captcha ??= await GetCaptchaAsync();
        using var form = new MultipartFormDataContent
        {
            { new StringContent(userName), "userName" },
            { new StringContent(email), "email" },
            { new StringContent(text), "text" },
            { new StringContent(captcha.Id), "captchaId" },
            { new StringContent(captchaAnswer ?? captcha.Answer!), "captchaAnswer" },
        };

        if (homePage is not null)
        {
            form.Add(new StringContent(homePage), "homePage");
        }

        if (parentId is not null)
        {
            form.Add(new StringContent(parentId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), "parentId");
        }

        if (file is { } f)
        {
            var content = new ByteArrayContent(f.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue(f.ContentType);
            form.Add(content, "file", f.Name);
        }

        return await http.PostAsync("/api/comments", form, Ct);
    }

    public async Task<CommentDto> PostOkAsync(string userName = "Tester", string email = "tester@example.com", string text = "Hello", long? parentId = null,
        (string Name, byte[] Content, string ContentType)? file = null)
    {
        using var response = await PostAsync(userName, email, text: text, parentId: parentId, file: file);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<CommentDto>(body, Json)!;
    }

    public async Task<T> GetAsync<T>(string url) => (await http.GetFromJsonAsync<T>(url, Json, Ct))!;
}

internal sealed record CaptchaDto(string Id, string Image, string? Answer);

internal sealed record ProblemDto(int Status, Dictionary<string, string[]> Errors);
