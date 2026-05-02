using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Akka;
using PersistenceRemoveDomain = Schleusenwerk.Persistence.RemoveDomain;

namespace Schleusenwerk.Routing;

/// <summary>
/// Persistent aggregate root for a single domain.
/// Handles domain configuration and upstream management via commands,
/// persists events, and publishes to EventHub.
/// States: WaitingForPublisher → Ready.
/// </summary>
public sealed class DomainEntityActor : ReceivePersistentActor, IWithUnboundedStash
{
    public override string PersistenceId
    {
        get
        {
            var name = Self.Path.Name;
            return $"domain-{name}";
        }
    }

    public new IStash Stash { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _upstreamRegion;
    private readonly IActorRef _eventHub;
    private readonly IConfigurationStore _configStore;
    private IMaterializer _materializer = null!;
    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;

    private DomainConfig? _config;
    private readonly List<UpstreamTarget> _upstreamTargets = [];
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private int _roundRobinIndex;

    public DomainEntityActor(IConfigurationStore configStore)
    {
        _configStore = configStore;
        _upstreamRegion = Context.GetActor<UpstreamEntityActor>();
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

            // Send RegisterUpstream for all recovered upstreams
            foreach (var upstream in _upstreamTargets)
            {
                _upstreamRegion.Tell(new RegisterUpstream(upstream));
            }

            Stash.UnstashAll();
            Become(Ready);
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
        Command<PersistenceRemoveDomain>(HandleRemoveDomain);
        Command<AddUpstream>(HandleAddUpstream);
        Command<RemoveUpstream>(HandleRemoveUpstream);
        Command<GetDomainConfig>(_ => HandleGetConfig());
        Command<GetDomainByName>(_ => HandleGetConfig());
        Command<ResolveUpstream>(HandleResolveUpstream);
        Command<UpstreamHealthChanged>(msg =>
        {
            if (msg.IsHealthy)
            {
                _unhealthyUrls.Remove(msg.Url);
            }
            else
            {
                _unhealthyUrls.Add(msg.Url);
            }
        });
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

    private void HandleRemoveDomain(PersistenceRemoveDomain cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.DomainName}' does not exist."));
            return;
        }

        var evt = new DomainDeactivated(cmd.DomainName);
        Persist(evt, persisted =>
        {
            _config = null;
            _upstreamTargets.Clear();
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
            _upstreamRegion.Tell(new RegisterUpstream(persisted.Target));
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

    private void HandleResolveUpstream(ResolveUpstream msg)
    {
        if (_config is null)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var healthy = _upstreamTargets.Where(u => !_unhealthyUrls.Contains(u.Url)).ToList();
        if (healthy.Count == 0)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var picked = healthy[_roundRobinIndex % healthy.Count];
        _roundRobinIndex++;
        Sender.Tell(new SelectUpstreamForDomain(_config, picked.Url.Value.ToString()));
    }

    private void PublishEvent(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Akka.Done.Instance,
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
