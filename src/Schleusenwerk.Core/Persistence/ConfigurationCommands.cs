using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

public sealed record AddDomain(DomainConfig Config) : IWithDomain
{
    public string Domain => Config.DomainName.Value;
}

public sealed record UpdateDomain(DomainConfig Config) : IWithDomain
{
    public string Domain => Config.DomainName.Value;
}

public sealed record RemoveDomain(DomainName DomainName) : IWithDomain
{
    public string Domain => DomainName.Value;
}

public sealed record AddUpstream(DomainName DomainName, UpstreamTarget Upstream) : IWithDomain
{
    public string Domain => DomainName.Value;
}

public sealed record RemoveUpstream(DomainName DomainName, UpstreamUrl UpstreamUrl) : IWithDomain
{
    public string Domain => DomainName.Value;
}

public sealed record UpdateSettings(ProxySettings Settings);

public sealed record ConfigurationCommandAck
{
    public static ConfigurationCommandAck Instance { get; } = new();
}

public sealed record ConfigurationCommandNack(string Reason);

public sealed record GetConfiguration
{
    public static GetConfiguration Instance { get; } = new();
}

public sealed record DomainConfigResult(DomainConfig Config, IReadOnlyList<UpstreamTarget> Upstreams);

public sealed record GetSettings
{
    public static GetSettings Instance { get; } = new();
}

public sealed record GetUpstreamByUrl(UpstreamUrl Url);

public sealed record UpstreamTargetResult(UpstreamTarget Target);

public sealed record GetAllDomains
{
    public static GetAllDomains Instance { get; } = new();
}

public sealed record AllDomainsResult(IReadOnlyList<DomainName> Domains);

public sealed record GetDomainConfig : IWithDomain
{
    public required string Domain { get; init; }
}
