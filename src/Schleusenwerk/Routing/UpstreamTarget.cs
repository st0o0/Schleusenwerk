namespace Schleusenwerk.Routing;

/// <summary>
/// A single upstream target with load balancing weight and connection limits.
/// </summary>
public sealed record UpstreamTarget
{
    public required UpstreamUrl Url { get; init; }
    public int Weight { get; init; } = 1;
    public int MaxConnections { get; init; } = 100;

    public static UpstreamTarget Create(string url, int weight = 1, int maxConnections = 100)
    {
        if (weight < 1)
            throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be at least 1.");

        if (maxConnections < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConnections), "MaxConnections must be at least 1.");

        return new UpstreamTarget
        {
            Url = UpstreamUrl.Parse(url),
            Weight = weight,
            MaxConnections = maxConnections,
        };
    }
}
