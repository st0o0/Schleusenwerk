namespace Schleusenwerk.Domain.LoadBalancing;

using Schleusenwerk.Domain.Routing;

/// <summary>
/// Request to select the next healthy upstream target.
/// </summary>
public sealed record SelectUpstream
{
    public static readonly SelectUpstream Instance = new();
}

/// <summary>
/// Response containing the selected upstream target.
/// </summary>
public sealed record UpstreamSelected(UpstreamTarget Target);

/// <summary>
/// Returned when no healthy upstream is available.
/// </summary>
public sealed record NoHealthyUpstreamAvailable
{
    public static readonly NoHealthyUpstreamAvailable Instance = new();
}

/// <summary>
/// Marks an upstream as unhealthy, removing it from the rotation.
/// </summary>
public sealed record MarkUpstreamUnhealthy(UpstreamUrl Url);

/// <summary>
/// Marks an upstream as healthy, adding it back to the rotation.
/// </summary>
public sealed record MarkUpstreamHealthy(UpstreamUrl Url);

/// <summary>
/// Replaces the entire upstream list. Resets health state.
/// </summary>
public sealed record UpdateUpstreams(IReadOnlyList<UpstreamTarget> Targets);
