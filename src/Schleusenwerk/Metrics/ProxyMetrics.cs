using System.Diagnostics.Metrics;

namespace Schleusenwerk.Metrics;

public sealed class ProxyMetrics
{
    private static readonly Meter Meter = new("Schleusenwerk.Proxy");

    private readonly Counter<long> _requestCounter = Meter.CreateCounter<long>("proxy.requests");
    private readonly Histogram<double> _requestDuration = Meter.CreateHistogram<double>("proxy.request.duration", "ms");
    private readonly Counter<long> _circuitBreakerTrips = Meter.CreateCounter<long>("proxy.circuit_breaker.trips");
    private readonly Counter<long> _rateLimitRejected = Meter.CreateCounter<long>("proxy.rate_limit.rejected");
    private readonly UpDownCounter<long> _upstreamHealth = Meter.CreateUpDownCounter<long>("proxy.upstream.health");

    public void RecordRequest(string domain, int statusCode)
    {
        _requestCounter.Add(1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("status_code", statusCode));
    }

    public void RecordDuration(string domain, string upstreamUrl, double durationMs)
    {
        _requestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("upstream_url", upstreamUrl));
    }

    public void RecordCircuitBreakerTrip(string domain, string upstreamUrl)
    {
        _circuitBreakerTrips.Add(1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("upstream_url", upstreamUrl));
    }

    public void RecordRateLimitRejected(string domain, string clientIp)
    {
        _rateLimitRejected.Add(1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("client_ip", clientIp));
    }

    public void RecordUpstreamHealthChange(string upstreamUrl, bool isHealthy)
    {
        _upstreamHealth.Add(isHealthy ? 1 : -1,
            new KeyValuePair<string, object?>("upstream_url", upstreamUrl));
    }
}
