using Schleusenwerk.Certificates;

namespace Schleusenwerk.Routing;

/// <summary>
/// Configuration for a single domain, including redirect behavior and options.
/// </summary>
public sealed record DomainConfig
{
    public required DomainName DomainName { get; init; }
    public RedirectMode HttpRedirect { get; init; } = RedirectMode.None;
    public Uri? RedirectUrl { get; init; }
    public bool ForceHttps { get; init; }
    public bool PreserveHostHeader { get; init; } = true;
    public bool WebSocketEnabled { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CircuitBreakerCooldown { get; init; } = TimeSpan.FromSeconds(30);
    public RateLimitConfig? RateLimit { get; init; }
    public TlsMode TlsMode { get; init; } = TlsMode.LetsEncrypt;
}

/// <summary>
/// Rate limiting configuration for a domain.
/// </summary>
public sealed record RateLimitConfig
{
    public int RequestsPerWindow { get; init; } = 100;
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(60);
}
