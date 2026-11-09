namespace Comments.Application.Abstractions;

public interface ICaptchaService
{
    Task<CaptchaChallenge> CreateAsync(CancellationToken cancellationToken);

    /// <summary>Checks the answer and consumes the challenge, so each picture can be tried once.</summary>
    Task<bool> VerifyAsync(string? id, string? answer, CancellationToken cancellationToken);
}

/// <summary>A CAPTCHA picture as PNG. <see cref="Answer"/> is set only when the test-answer option is on (load tests).</summary>
public sealed record CaptchaChallenge(string Id, byte[] Png, string? Answer);
