using Akka.Actor;
using Akka.Event;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;

namespace Schleusenwerk.HealthCheck;

/// <summary>
/// Monitors a single upstream target by periodically probing its health endpoint.
/// Tells <see cref="UpstreamHealthChanged"/> to the EventHubActor when the health
/// status transitions between healthy and unhealthy based on configurable thresholds.
/// </summary>
public sealed class HealthCheckActor : ReceiveActor, IWithTimers
{
    private const string TimerKey = "health-check-tick";

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly UpstreamUrl _upstreamUrl;
    private readonly HealthCheckConfig _config;
    private readonly Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> _probeFunc;
    private readonly IActorRef _eventHub;

    private bool _isHealthy = true;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;

    public ITimerScheduler Timers { get; set; } = null!;

    public HealthCheckActor(
        UpstreamUrl upstreamUrl,
        HealthCheckConfig config,
        Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> probeFunc)
    {
        _upstreamUrl = upstreamUrl;
        _config = config;
        _probeFunc = probeFunc;
        _eventHub = Context.GetActor<EventHub>();

        Receive<CheckHealth>(_ => OnCheckHealth());
        Receive<GetHealthStatus>(_ => OnGetHealthStatus());
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer(TimerKey, CheckHealth.Instance, _config.Interval, _config.Interval);
    }

    private void OnCheckHealth()
    {
        var self = Self;
        var endpoint = _config.HealthEndpoint;
        var timeout = _config.Timeout;

        // Fire-and-forget with PipeTo so result is processed on actor thread
        _probeFunc(_upstreamUrl, endpoint, timeout, CancellationToken.None)
            .ContinueWith(task => task is { IsCompletedSuccessfully: true, Result: true })
            .PipeTo(self);

        // Handle the piped boolean result
        Become(() =>
        {
            Receive<bool>(HandleProbeResult);
            Receive<CheckHealth>(_ => { }); // Ignore ticks while waiting for probe result
            Receive<GetHealthStatus>(_ => OnGetHealthStatus());
        });
    }

    private void HandleProbeResult(bool success)
    {
        if (success)
        {
            _consecutiveFailures = 0;
            _consecutiveSuccesses++;

            if (!_isHealthy && _consecutiveSuccesses >= _config.HealthyThreshold)
            {
                _isHealthy = true;
                _log.Info("Upstream {Url} is now healthy after {Count} consecutive successes",
                    _upstreamUrl, _consecutiveSuccesses);
                _eventHub.Tell(new UpstreamHealthChanged(_upstreamUrl, IsHealthy: true));
            }
        }
        else
        {
            _consecutiveSuccesses = 0;
            _consecutiveFailures++;

            if (_isHealthy && _consecutiveFailures >= _config.UnhealthyThreshold)
            {
                _isHealthy = false;
                _log.Warning("Upstream {Url} is now unhealthy after {Count} consecutive failures",
                    _upstreamUrl, _consecutiveFailures);
                _eventHub.Tell(new UpstreamHealthChanged(_upstreamUrl, IsHealthy: false));
            }
        }

        // Restore normal receive behavior
        Become(() =>
        {
            Receive<CheckHealth>(_ => OnCheckHealth());
            Receive<GetHealthStatus>(_ => OnGetHealthStatus());
        });
    }

    private void OnGetHealthStatus()
    {
        Sender.Tell(new HealthStatus(_upstreamUrl, _isHealthy, _consecutiveFailures, _consecutiveSuccesses));
    }
}