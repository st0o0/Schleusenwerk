namespace Schleusenwerk.Routing;

public sealed record RegisterUpstream(UpstreamTarget Target) : IWithUrl
{
    public string Url => Target.Url.Value.ToString();
}

public sealed record SelectUpstreamForDomain(DomainConfig Config, string Url) : IWithUrl;

public sealed record ResolveUpstream(string Host) : IWithDomain
{
    public string Domain => Host.ToLowerInvariant();
}

public sealed record UpstreamResolved(UpstreamTarget Target, DomainConfig Config);

public sealed record UpstreamNotFound(string Host);

public sealed record RoutesUpdated(IReadOnlyList<DomainName> Domains);

public sealed record RouteRemoved(DomainName DomainName);
