using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Add a new domain configuration. Fails if domain already exists.
/// </summary>
public sealed record AddDomain(DomainConfig Config);

/// <summary>
/// Update an existing domain configuration. Fails if domain does not exist.
/// </summary>
public sealed record UpdateDomain(DomainConfig Config);

/// <summary>
/// Remove a domain and all its upstreams. Fails if domain does not exist.
/// </summary>
public sealed record RemoveDomain(DomainName DomainName);

/// <summary>
/// Add an upstream target to an existing domain. Fails if domain does not exist or upstream already present.
/// </summary>
public sealed record AddUpstream(DomainName DomainName, UpstreamTarget Upstream);

/// <summary>
/// Remove an upstream target from a domain. Fails if domain or upstream does not exist.
/// </summary>
public sealed record RemoveUpstream(DomainName DomainName, UpstreamUrl UpstreamUrl);

/// <summary>
/// Update global proxy settings.
/// </summary>
public sealed record UpdateSettings(ProxySettings Settings);

/// <summary>
/// Successful command acknowledgment.
/// </summary>
public sealed record ConfigurationCommandAck
{
    public static ConfigurationCommandAck Instance { get; } = new();
}

/// <summary>
/// Command rejection with reason.
/// </summary>
public sealed record ConfigurationCommandNack(string Reason);

/// <summary>
/// Query to retrieve the full current configuration state.
/// </summary>
public sealed record GetConfiguration
{
    public static GetConfiguration Instance { get; } = new();
}

/// <summary>
/// Query to retrieve configuration for a single domain.
/// Returns <see cref="DomainConfigResult"/> or <see cref="ConfigurationCommandNack"/> if not found.
/// </summary>
public sealed record GetDomainByName(DomainName DomainName);

/// <summary>
/// Result of a <see cref="GetDomainByName"/> query.
/// </summary>
public sealed record DomainConfigResult(DomainConfig Config, IReadOnlyList<UpstreamTarget> Upstreams);

/// <summary>
/// Query to retrieve the current proxy settings.
/// </summary>
public sealed record GetSettings
{
    public static GetSettings Instance { get; } = new();
}
