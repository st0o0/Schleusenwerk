namespace Schleusenwerk.Routing;

/// <summary>
/// Updates or adds route definitions. Existing domains are overwritten.
/// </summary>
public sealed record UpdateRoutes(IReadOnlyList<RouteDefinition> Routes);

/// <summary>
/// Resolves the upstream route for a given host header value.
/// </summary>
public sealed record ResolveUpstream(string Host);

/// <summary>
/// Removes a domain from the routing table.
/// </summary>
public sealed record RemoveDomain(DomainName DomainName);

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
