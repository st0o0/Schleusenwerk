using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Routing;

public sealed class DomainEntityActor : ReceivePersistentActor, IWithUnboundedStash
{
    public override string PersistenceId => $"domain-{Self.Path.Name}";

    public new IStash Stash { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _healthCheckRegion;
    private readonly IActorRef _eventHub;
    private readonly IConfigurationStore _configStore;
    private IMaterializer _materializer = null!;
    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;

    private DomainConfig? _config;
    private readonly List<UpstreamTarget> _upstreamTargets = [];
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private readonly Dictionary<UpstreamUrl, UpstreamCircuitState> _circuitStates = new();
    private int _roundRobinIndex;

    public DomainEntityActor(IConfigurationStore configStore)
    {
        _configStore = configStore;
        _healthCheckRegion = Context.GetActor<HealthCheckEntityActor>();
        _eventHub = Context.GetActor<EventHub>();

        Recover<DomainConfigured>(evt => _config = evt.Config);
        Recover<DomainUpstreamAdded>(evt => _upstreamTargets.Add(evt.Target));
        Recover<DomainUpstreamRemoved>(evt => _upstreamTargets.RemoveAll(t => t.Url.Equals(evt.Url)));
        Recover<DomainDeactivated>(_ =>
        {
            _config = null;
            _upstreamTargets.Clear();
        });
        Recover<SnapshotOffer>(_ => { });

        WaitingForPublisher();
    }

    protected override void PreStart()
    {
        base.PreStart();
        _materializer = Context.System.Materializer();
        _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
            .PipeTo(Self);
    }

    private UpstreamCircuitState GetCircuitState(UpstreamUrl url)
    {
        if (!_circuitStates.TryGetValue(url, out var state))
        {
            state = new UpstreamCircuitState(url, TimeSpan.FromSeconds(30));
            _circuitStates[url] = state;
        }
        return state;
    }

    private void WaitingForPublisher()
    {
        Command<EventHub.PublisherReady>(msg =>
        {
            var sink = msg.SinkRef.Sink;
            _publishQueue = Source.Queue<IClusterEvent>(100, OverflowStrategy.DropHead)
                .To(sink)
                .Run(_materializer);

            _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe<IDomainEvent>.Instance)
                .PipeTo(Self);

            SubscribeToHealthChecks();

            Become(Ready);
            Stash.UnstashAll();
        });
        Command<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "Failed to get publisher from EventHub — retrying");
            _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
                .PipeTo(Self);
        });
        CommandAny(_ => Stash.Stash());
    }

    private void Ready()
    {
        Command<EventHub.Subscribed>(msg =>
        {
            msg.SourceRef.Source.RunWith(Sink.ActorRef<IClusterEvent>(Self, StreamCompleted.Instance, ex => new StreamFailed(ex)), _materializer);
        });
        Command<AddDomain>(HandleAddDomain);
        Command<UpdateDomain>(HandleUpdateDomain);
        Command<RemoveDomain>(HandleRemoveDomain);
        Command<AddUpstream>(HandleAddUpstream);
        Command<RemoveUpstream>(HandleRemoveUpstream);
        Command<GetDomainConfig>(_ => HandleGetConfig());
        Command<GetDomainUpstreamHealth>(_ => HandleGetUpstreamHealth());
        Command<ResolveUpstream>(HandleResolveUpstream);
        Command<RequestFailed>(msg =>
        {
            var state = GetCircuitState(msg.Url);
            state.RecordFailure();
        });
        Command<RequestSucceeded>(msg =>
        {
            var state = GetCircuitState(msg.Url);
            state.RecordSuccess();
        });
        Command<UpstreamHealthChanged>(msg =>
        {
            if (msg.IsHealthy)
            {
                _unhealthyUrls.Remove(msg.Url);
                if (_circuitStates.TryGetValue(msg.Url, out var state))
                {
                    state.ForceClose();
                }
            }
            else
            {
                _unhealthyUrls.Add(msg.Url);
                GetCircuitState(msg.Url).ForceOpen();
            }
        });
        Command<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "Stream or async operation failed");
        });
        Command<StreamCompleted>(_ => { });
        Command<StreamFailed>(f =>
        {
            _log.Warning(f.Ex, "Event subscription stream failed");
        });
        Command<PublishDropped>(_ => { });
        Command<PublishFailed>(f =>
        {
            _log.Warning(f.Exception, "Failed to publish event to hub");
        });
        Command<IDomainEvent>(_ => { });
    }

    private void SubscribeToHealthChecks()
    {
        foreach (var upstream in _upstreamTargets)
        {
            _healthCheckRegion.Tell(new SubscribeHealth(Self) { Url = upstream.Url.Value.ToString() });
        }
    }

    private void HandleAddDomain(AddDomain cmd)
    {
        if (_config is not null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.Config.DomainName}' already configured."));
            return;
        }

        var evt = new DomainConfigured(cmd.Config);
        Persist(evt, persisted =>
        {
            _config = persisted.Config;
            _configStore.UpsertDomainAsync(persisted.Config);
            PublishEvent(persisted);
            PublishEvent(new CertificateProvisioningRequested(cmd.Config.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleUpdateDomain(UpdateDomain cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.Config.DomainName}' does not exist."));
            return;
        }

        var evt = new DomainConfigured(cmd.Config);
        Persist(evt, persisted =>
        {
            _config = persisted.Config;
            _configStore.UpsertDomainAsync(persisted.Config);
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleRemoveDomain(RemoveDomain cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.DomainName}' does not exist."));
            return;
        }

        foreach (var upstream in _upstreamTargets)
        {
            _healthCheckRegion.Tell(new UnsubscribeHealth(Self) { Url = upstream.Url.Value.ToString() });
        }

        var evt = new DomainDeactivated(cmd.DomainName);
        Persist(evt, persisted =>
        {
            _config = null;
            _upstreamTargets.Clear();
            _circuitStates.Clear();
            _configStore.RemoveDomainAsync(cmd.DomainName);
            PublishEvent(persisted);
            Context.System.EventStream.Publish(new RouteRemoved(cmd.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
            Self.Tell(PoisonPill.Instance);
        });
    }

    private void HandleAddUpstream(AddUpstream cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack("Domain not configured yet."));
            return;
        }

        if (_upstreamTargets.Any(t => t.Url.Equals(cmd.Upstream.Url)))
        {
            Sender.Tell(new ConfigurationCommandNack($"Upstream '{cmd.Upstream.Url}' already exists."));
            return;
        }

        var evt = new DomainUpstreamAdded(cmd.Upstream);
        Persist(evt, persisted =>
        {
            _upstreamTargets.Add(persisted.Target);
            _healthCheckRegion.Tell(new SubscribeHealth(Self) { Url = persisted.Target.Url.Value.ToString() });
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleRemoveUpstream(RemoveUpstream cmd)
    {
        if (!_upstreamTargets.Any(t => t.Url.Equals(cmd.UpstreamUrl)))
        {
            Sender.Tell(new ConfigurationCommandNack($"Upstream '{cmd.UpstreamUrl}' does not exist."));
            return;
        }

        var evt = new DomainUpstreamRemoved(cmd.UpstreamUrl);
        Persist(evt, persisted =>
        {
            _upstreamTargets.RemoveAll(t => t.Url.Equals(persisted.Url));
            _unhealthyUrls.Remove(persisted.Url);
            _circuitStates.Remove(persisted.Url);
            _healthCheckRegion.Tell(new UnsubscribeHealth(Self) { Url = persisted.Url.Value.ToString() });
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleGetConfig()
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack("Domain not configured."));
            return;
        }

        Sender.Tell(new DomainConfigResult(_config, _upstreamTargets));
    }

    private void HandleGetUpstreamHealth()
    {
        var entries = _upstreamTargets
            .Select(t => new UpstreamHealthStatus(t.Url, !_unhealthyUrls.Contains(t.Url)))
            .ToList();
        Sender.Tell(new DomainUpstreamHealthResult(entries));
    }

    private void HandleResolveUpstream(ResolveUpstream msg)
    {
        if (_config is null)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var available = _upstreamTargets
            .Where(u => !_unhealthyUrls.Contains(u.Url) && GetCircuitState(u.Url).IsAvailable)
            .ToList();

        if (available.Count == 0)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var picked = available[_roundRobinIndex % available.Count];
        _roundRobinIndex++;
        Sender.Tell(new UpstreamResolved(picked, _config));
    }

    private void PublishEvent(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Done.Instance,
            failure: ex => new PublishFailed(ex));
    }

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Ex);

    private sealed record PublishDropped(IClusterEvent Event);
    private sealed record PublishFailed(Exception Exception);
}
