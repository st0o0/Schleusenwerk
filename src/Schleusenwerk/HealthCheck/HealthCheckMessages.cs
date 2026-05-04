using Schleusenwerk.Routing;

namespace Schleusenwerk.HealthCheck;

/// <summary>
/// Published to EventStream when an upstream's health status changes.
/// </summary>
public sealed record UpstreamHealthChanged(UpstreamUrl Url, bool IsHealthy) : IDomainEvent, IUpstreamEvent;

/// <summary>
/// Internal tick message that triggers a health check probe.
/// </summary>
public sealed record CheckHealth
{
    public static readonly CheckHealth Instance = new();
}

/// <summary>
/// Requests the current health status of the monitored upstream.
/// </summary>
public sealed record GetHealthStatus
{
    public static readonly GetHealthStatus Instance = new();
}

/// <summary>
/// Response containing the current health status of the upstream.
/// </summary>
public sealed record HealthStatus(UpstreamUrl Url, bool IsHealthy, int ConsecutiveFailures, int ConsecutiveSuccesses);
