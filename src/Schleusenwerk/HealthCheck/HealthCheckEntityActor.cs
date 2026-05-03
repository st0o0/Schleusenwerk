using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;

namespace Schleusenwerk.HealthCheck;

public sealed class HealthCheckEntityActor : ReceiveActor, IWithTimers, IWithUnboundedStash
{
    private const string TimerKey = "health-check-tick";

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly UpstreamTarget _target;
    private readonly HealthCheckConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IActorRef _eventHub;
    private readonly HashSet<IActorRef> _subscribers = [];

    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;
    private IMaterializer _materializer = null!;

    private bool _isHealthy = true;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;

    public ITimerScheduler Timers { get; set; } = null!;
    public IStash Stash { get; set; } = null!;

    public HealthCheckEntityActor(UpstreamTarget target, IHttpClientFactory httpClientFactory)
    {
        _target = target;
        _config = target.HealthCheck;
        _httpClientFactory = httpClientFactory;
        _eventHub = Context.GetActor<EventHub>();

        WaitingForPublisher();
    }

    protected override void PreStart()
    {
        _materializer = Context.System.Materializer();
        _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
            .PipeTo(Self);
    }

    private void WaitingForPublisher()
    {
        Receive<EventHub.PublisherReady>(msg =>
        {
            var sink = msg.SinkRef.Sink;
            _publishQueue = Source.Queue<IClusterEvent>(100, OverflowStrategy.DropHead)
                .To(sink)
                .Run(_materializer);

            Timers.StartPeriodicTimer(TimerKey, CheckHealth.Instance, _config.Interval, _config.Interval);

            _log.Info("HealthCheckEntity publisher ready for {Url}", _target.Url);
            Stash.UnstashAll();
            Become(Idle);
        });
        Receive<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "Failed to get publisher from EventHub — retrying");
            _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
                .PipeTo(Self);
        });
        ReceiveAny(_ => Stash.Stash());
    }

    private void Idle()
    {
        Receive<CheckHealth>(_ => OnCheckHealth());
        Receive<GetHealthStatus>(_ => OnGetHealthStatus());
        Receive<SubscribeHealth>(OnSubscribe);
        Receive<UnsubscribeHealth>(OnUnsubscribe);
        Receive<Terminated>(OnTerminated);
    }

    private void Probing()
    {
        Receive<bool>(HandleProbeResult);
        Receive<CheckHealth>(_ => { });
        Receive<GetHealthStatus>(_ => OnGetHealthStatus());
        Receive<SubscribeHealth>(OnSubscribe);
        Receive<UnsubscribeHealth>(OnUnsubscribe);
        Receive<Terminated>(OnTerminated);
    }

    private void OnSubscribe(SubscribeHealth msg)
    {
        if (_subscribers.Add(msg.Subscriber))
        {
            Context.Watch(msg.Subscriber);
        }
    }

    private void OnUnsubscribe(UnsubscribeHealth msg)
    {
        if (_subscribers.Remove(msg.Subscriber))
        {
            Context.Unwatch(msg.Subscriber);
        }
    }

    private void OnTerminated(Terminated msg)
    {
        _subscribers.Remove(msg.ActorRef);
    }

    private void OnCheckHealth()
    {
        var self = Self;
        var url = _target.Url;
        var endpoint = _config.HealthEndpoint;
        var timeout = _config.Timeout;

        Task.Run(async () =>
        {
            using var client = _httpClientFactory.CreateClient("health-check");
            client.Timeout = timeout;
            try
            {
                var uri = new Uri($"{url}{endpoint.TrimStart('/')}");
                using var response = await client.GetAsync(uri);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }).PipeTo(self);

        Become(Probing);
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
                    _target.Url, _consecutiveSuccesses);
                var evt = new UpstreamHealthChanged(_target.Url, IsHealthy: true);
                NotifySubscribers(evt);
                PublishToEventHub(evt);
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
                    _target.Url, _consecutiveFailures);
                var evt = new UpstreamHealthChanged(_target.Url, IsHealthy: false);
                NotifySubscribers(evt);
                PublishToEventHub(evt);
            }
        }

        Become(Idle);
    }

    private void NotifySubscribers(UpstreamHealthChanged evt)
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber.Tell(evt);
        }
    }

    private void OnGetHealthStatus()
    {
        Sender.Tell(new HealthStatus(_target.Url, _isHealthy, _consecutiveFailures, _consecutiveSuccesses));
    }

    private void PublishToEventHub(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Done.Instance,
            failure: ex => new PublishFailed(ex));
    }

    private sealed record PublishDropped(IClusterEvent Event);
    private sealed record PublishFailed(Exception Exception);
}
