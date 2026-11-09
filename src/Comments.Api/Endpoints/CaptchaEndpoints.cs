using System.Text.Json.Serialization;
using Comments.Api.Hosting;
using Comments.Application.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Comments.Api.Endpoints;

internal static class CaptchaEndpoints
{
    public static IEndpointRouteBuilder MapCaptchaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/captcha", CreateAsync)
            .WithTags("Captcha")
            .RequireRateLimiting(RateLimiting.CaptchaPolicy)
            .WithSummary("A new CAPTCHA picture. Its id goes with the answer when posting; each picture can be tried once.");
        return app;
    }

    private static async Task<Results<Ok<CaptchaResponse>, ProblemHttpResult>> CreateAsync(ICaptchaService captcha, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        CaptchaChallenge challenge;
        try
        {
            challenge = await captcha.CreateAsync(cancellationToken);
        }
        catch (CaptchaUnavailableException)
        {
            return TypedResults.Problem(CaptchaUnavailableException.UserMessage, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return TypedResults.Ok(new CaptchaResponse(
            challenge.Id,
            "data:image/png;base64," + Convert.ToBase64String(challenge.Png),
            challenge.Answer));
    }
}

internal sealed record CaptchaResponse(
    string Id,
    string Image,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Answer);
