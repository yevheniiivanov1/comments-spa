using System.Security.Cryptography;
using System.Text;
using Comments.Application.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Comments.Infrastructure.Captcha;

public sealed class CaptchaOptions
{
    public const string Section = "Captcha";

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Returns the answer with the picture so a load test can post comments. Never enable it on a public deployment:
    /// it turns the CAPTCHA off.
    /// </summary>
    public bool ExposeAnswerForTesting { get; set; }
}

/// <summary>
/// CAPTCHA of Latin letters and digits. The expected answer lives in Redis under a random id for a few minutes and is
/// removed by the first check (GETDEL), so a solved picture cannot be replayed. Answers are case-insensitive.
/// </summary>
internal sealed class CaptchaService(IConnectionMultiplexer redis, CaptchaRenderer renderer, IOptions<CaptchaOptions> options) : ICaptchaService
{
    public const int Length = 5;

    /// <summary>Latin letters and digits without look-alikes (0/O, 1/I/l).</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public async Task<CaptchaChallenge> CreateAsync(CancellationToken cancellationToken)
    {
        var answer = RandomNumberGenerator.GetString(Alphabet, Length);
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        try
        {
            await redis.GetDatabase().StringSetAsync(Key(id), Normalize(answer), options.Value.Lifetime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CaptchaUnavailableException(ex);
        }

        return new CaptchaChallenge(id, renderer.Render(answer), options.Value.ExposeAnswerForTesting ? answer : null);
    }

    public async Task<bool> VerifyAsync(string? id, string? answer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || string.IsNullOrWhiteSpace(answer) || answer.Length > 16)
        {
            return false;
        }

        RedisValue expected;
        try
        {
            expected = await redis.GetDatabase().StringGetDeleteAsync(Key(id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CaptchaUnavailableException(ex);
        }
        if (!expected.HasValue)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected.ToString()),
            Encoding.ASCII.GetBytes(Normalize(answer.Trim())));
    }

    private static string Normalize(string answer) => answer.ToUpperInvariant();

    private static string Key(string id) => $"captcha:{id}";
}
