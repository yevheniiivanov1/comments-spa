namespace Comments.Domain.Comments;

/// <summary>What identifies the client that posted a comment besides the form fields.</summary>
public sealed record ClientInfo
{
    public const int MaxIpAddressLength = 45;
    public const int MaxUserAgentLength = 512;

    private ClientInfo(string ipAddress, string? userAgent)
    {
        IpAddress = ipAddress;
        UserAgent = userAgent;
    }

    public string IpAddress { get; }

    public string? UserAgent { get; }

    public static ClientInfo Create(string? ipAddress, string? userAgent)
    {
        var ip = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        var agent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();
        return new ClientInfo(
            ip.Length > MaxIpAddressLength ? ip[..MaxIpAddressLength] : ip,
            agent is { Length: > MaxUserAgentLength } ? agent[..MaxUserAgentLength] : agent);
    }
}
