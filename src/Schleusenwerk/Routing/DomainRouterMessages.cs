namespace Schleusenwerk.Routing;

public interface IWithEntityId
{
    string EntityId { get; }
}

public interface IWithDomain : IWithEntityId
{
    string Domain { get; }
    string IWithEntityId.EntityId => Domain;
}

public interface IWithUrl : IWithEntityId
{
    string Url { get; }
    string IWithEntityId.EntityId => Url;
}

/// <summary>
/// Sets or replaces the route for a single domain. Sent by ConfigurationPersistenceActor.
/// </summary>
public sealed record SetRoute(DomainConfig Config, IReadOnlyList<UpstreamTarget> Upstreams) : IWithDomain
{
    public string Domain => Config.DomainName.Value;
}

/// <summary>
/// Registers an upstream URL with its entity actor. Sent by DomainEntityActor on SetRoute.
/// </summary>
public sealed record RegisterUpstream(UpstreamTarget Target) : IWithUrl
{
    public string Url => Target.Url.Value.ToString();
}

/// <summary>
/// Forwarded from DomainEntityActor to UpstreamEntityActor, preserving the original sender.
/// </summary>
public sealed record SelectUpstreamForDomain(DomainConfig Config, string Url) : IWithUrl;

/// <summary>
/// Resolves the upstream route for a given host header value.
/// </summary>
public sealed record ResolveUpstream(string Host) : IWithDomain
{
    public string Domain => Host.ToLowerInvariant();
}

/// <summary>
/// Removes a domain from the routing table.
/// </summary>
public sealed record RemoveDomain(DomainName DomainName) : IWithDomain
{
    public string Domain => DomainName.Value;
}

/// <summary>
/// Query to retrieve configuration for a single domain.
/// Returns <see cref="DomainConfigResult"/> or <see cref="ConfigurationCommandNack"/> if not found.
/// Routed to DomainEntityActor shard region via IWithDomain.
/// </summary>
public sealed record GetDomainByName(DomainName DomainName) : IWithDomain
{
    public string Domain => DomainName.Value;
}

/// <summary>
/// Result of a successful upstream resolution.
/// </summary>
public sealed record UpstreamResolved(UpstreamTarget Target, DomainConfig Config);

/// <summary>
/// Result when no upstream is found for the requested host.
/// </summary>
public sealed record UpstreamNotFound(string Host);

/// <summary>
/// Published to EventStream when routes are added or updated.
/// </summary>
public sealed record RoutesUpdated(IReadOnlyList<DomainName> Domains);

/// <summary>
/// Published to EventStream when a domain is removed.
/// </summary>
public sealed record RouteRemoved(DomainName DomainName);
