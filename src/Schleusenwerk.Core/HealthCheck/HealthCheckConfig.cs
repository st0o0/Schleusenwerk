namespace Schleusenwerk.HealthCheck;

/// <summary>
/// Configuration for periodic upstream health checks.
/// </summary>
public sealed record HealthCheckConfig
{
    /// <summary>
    /// Interval between health check probes. Default: 30 seconds.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Consecutive failures required before marking an upstream unhealthy.
    /// </summary>
    public int UnhealthyThreshold { get; init; } = 3;

    /// <summary>
    /// Consecutive successes required before marking an upstream healthy again.
    /// </summary>
    public int HealthyThreshold { get; init; } = 2;

    /// <summary>
    /// Path appended to the upstream URL for health probes. Default: "/".
    /// </summary>
    public string HealthEndpoint { get; init; } = "/";

    /// <summary>
    /// Timeout for individual health check requests.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// When true, uses HTTP HEAD; otherwise HTTP GET.
    /// </summary>
    public bool UseHead { get; init; } = true;
}
