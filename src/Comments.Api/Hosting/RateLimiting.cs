using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Comments.Api.Hosting;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public int PostsPerMinute { get; set; } = 10;

    public int CaptchasPerMinute { get; set; } = 30;

    public int PreviewsPerMinute { get; set; } = 60;

    public int SearchesPerMinute { get; set; } = 120;
}

/// <summary>
/// Per-client-IP limits on the endpoints that write or burn CPU. They are per instance; behind several instances
/// the effective limit multiplies, which is acceptable for abuse protection (the CAPTCHA is the real gate).
/// </summary>
internal static class RateLimiting
{
    public const string PostPolicy = "post";
    public const string CaptchaPolicy = "captcha";
    public const string PreviewPolicy = "preview";
    public const string SearchPolicy = "search";

    public static IServiceCollection AddCommentsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddPerIp(options, PostPolicy, limits.PostsPerMinute);
            AddPerIp(options, CaptchaPolicy, limits.CaptchasPerMinute);
            AddPerIp(options, PreviewPolicy, limits.PreviewsPerMinute);
            AddPerIp(options, SearchPolicy, limits.SearchesPerMinute);
        });
    }

    private static void AddPerIp(RateLimiterOptions options, string policy, int permitsPerMinute) =>
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
}
