namespace Schleusenwerk.Domain.Routing;

/// <summary>
/// Maps a domain configuration to one or more upstream targets.
/// </summary>
public sealed record RouteDefinition
{
    public required DomainConfig Config { get; init; }
    public required IReadOnlyList<UpstreamTarget> Upstreams { get; init; }

    public DomainName DomainName => Config.DomainName;

    public static RouteDefinition Create(DomainConfig config, IReadOnlyList<UpstreamTarget> upstreams)
    {
        if (upstreams.Count == 0)
            throw new ArgumentException("At least one upstream target is required.", nameof(upstreams));

        return new RouteDefinition
        {
            Config = config,
            Upstreams = upstreams,
        };
    }
}
