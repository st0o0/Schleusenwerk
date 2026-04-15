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
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
