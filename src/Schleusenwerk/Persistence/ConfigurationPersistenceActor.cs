using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Routing;
using RoutingRemoveDomain = Schleusenwerk.Routing.RemoveDomain;
using Servus.Akka;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Event-sourced persistent actor that manages all domain configuration state.
/// Commands are validated, persisted as events, and applied to in-memory state.
/// Stashes commands until a publisher channel to EventHubActor is established.
/// Publishes IClusterEvent messages via Source.Queue → ISinkRef → EventHubActor MergeHub.
/// </summary>
public sealed class ConfigurationPersistenceActor : ReceivePersistentActor, IWithUnboundedStash
{
    public override string PersistenceId => "configuration";
    public new IStash Stash { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ConfigurationState _state = new();
    private readonly int _snapshotInterval;
    private readonly int _keepSnapshots;
    private readonly IActorRef _eventHub;
    private IMaterializer _materializer = null!;
    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;
    private IActorRef _domainRegion = null!;

    public ConfigurationPersistenceActor(int snapshotInterval = 100, int keepSnapshots = 3)
    {
        _eventHub = Context.GetActor<EventHub>();
        _snapshotInterval = snapshotInterval;
        _keepSnapshots = keepSnapshots;

        Recover<DomainAdded>(evt => _state.Apply(evt));
        Recover<DomainUpdated>(evt => _state.Apply(evt));
        Recover<DomainRemoved>(evt => _state.Apply(evt));
        Recover<UpstreamAdded>(evt => _state.Apply(evt));
        Recover<UpstreamRemoved>(evt => _state.Apply(evt));
        Recover<SettingsUpdated>(evt => _state.Apply(evt));
        Recover<SnapshotOffer>(offer =>
        {
            if (offer.Snapshot is ConfigurationSnapshot snapshot)
            {
                _state.RestoreFromSnapshot(snapshot);
                _log.Info("Restored configuration from snapshot at sequence {SequenceNr}", offer.Metadata.SequenceNr);
            }
        });

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
            _domainRegion = Context.GetActor<DomainEntityActor>();

            foreach (var (domainName, domainConfig) in _state.Domains)
            {
                var upstreams = _state.Upstreams.TryGetValue(domainName, out var list)
                    ? list
                    : (IReadOnlyList<UpstreamTarget>)[];
                _domainRegion.Tell(new SetRoute(domainConfig, upstreams));
            }

            _log.Info("Publisher channel to EventHubActor established; replayed {Count} route(s)", _state.Domains.Count);
            Become(Ready);
            Stash.UnstashAll();
        });
        Command<Status.Failure>(f =>
        {
            _log.Error(f.Cause, "Failed to get publisher from EventHubActor — retrying");
            _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
                .PipeTo(Self);
        });
        CommandAny(_ => Stash.Stash());
    }

    private void Ready()
    {
        Command<AddDomain>(Handle);
        Command<UpdateDomain>(Handle);
        Command<RemoveDomain>(Handle);
        Command<AddUpstream>(Handle);
        Command<RemoveUpstream>(Handle);
        Command<UpdateSettings>(Handle);
        Command<GetConfiguration>(Handle);
        Command<GetDomainByName>(Handle);
        Command<GetSettings>(Handle);
        Command<SaveSnapshotSuccess>(msg =>
        {
            var upperBound = new SnapshotSelectionCriteria(
                msg.Metadata.SequenceNr - _keepSnapshots * _snapshotInterval);
            DeleteSnapshots(upperBound);
        });
        Command<SaveSnapshotFailure>(msg =>
            _log.Error(msg.Cause, "Failed to save snapshot at sequence {SequenceNr}", msg.Metadata.SequenceNr));
        Command<DeleteSnapshotsSuccess>(_ => { });
        Command<DeleteSnapshotsFailure>(msg =>
            _log.Warning(msg.Cause, "Failed to delete old snapshots"));
    }

    private void Handle(AddDomain cmd)
    {
        var validation = ConfigurationValidator.ValidateAddDomain(cmd.Config, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new DomainAdded(cmd.Config);
        PersistAndApply(evt, () =>
        {
            _log.Info("Domain added: {Domain}", cmd.Config.DomainName);
            PublishClusterEvent(evt);
            PublishClusterEvent(new CertificateProvisioningRequested(cmd.Config.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(UpdateDomain cmd)
    {
        var validation = ConfigurationValidator.ValidateUpdateDomain(cmd.Config, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new DomainUpdated(cmd.Config);
        PersistAndApply(evt, () =>
        {
            _log.Info("Domain updated: {Domain}", cmd.Config.DomainName);
            PublishClusterEvent(evt);
            var upstreams = _state.Upstreams.TryGetValue(cmd.Config.DomainName, out var list)
                ? list
                : (IReadOnlyList<UpstreamTarget>)[];
            _domainRegion.Tell(new SetRoute(_state.Domains[cmd.Config.DomainName], upstreams));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(RemoveDomain cmd)
    {
        var validation = ConfigurationValidator.ValidateRemoveDomain(cmd.DomainName, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new DomainRemoved(cmd.DomainName);
        PersistAndApply(evt, () =>
        {
            _log.Info("Domain removed: {Domain}", cmd.DomainName);
            PublishClusterEvent(evt);
            _domainRegion.Tell(new RoutingRemoveDomain(cmd.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(AddUpstream cmd)
    {
        var validation = ConfigurationValidator.ValidateAddUpstream(cmd.DomainName, cmd.Upstream, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new UpstreamAdded(cmd.DomainName, cmd.Upstream);
        PersistAndApply(evt, () =>
        {
            _log.Info("Upstream added to {Domain}: {Url}", cmd.DomainName, cmd.Upstream.Url);
            var upstreams = _state.Upstreams[cmd.DomainName];
            _domainRegion.Tell(new SetRoute(_state.Domains[cmd.DomainName], upstreams));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(RemoveUpstream cmd)
    {
        var validation = ConfigurationValidator.ValidateRemoveUpstream(cmd.DomainName, cmd.UpstreamUrl, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new UpstreamRemoved(cmd.DomainName, cmd.UpstreamUrl);
        PersistAndApply(evt, () =>
        {
            _log.Info("Upstream removed from {Domain}: {Url}", cmd.DomainName, cmd.UpstreamUrl);
            var upstreams = _state.Upstreams.TryGetValue(cmd.DomainName, out var list)
                ? list
                : (IReadOnlyList<UpstreamTarget>)[];
            _domainRegion.Tell(new SetRoute(_state.Domains[cmd.DomainName], upstreams));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(UpdateSettings cmd)
    {
        var evt = new SettingsUpdated(cmd.Settings);
        PersistAndApply(evt, () =>
        {
            _log.Info("Proxy settings updated");
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(GetConfiguration _)
    {
        Sender.Tell(_state.ToSnapshot());
    }

    private void Handle(GetDomainByName query)
    {
        if (!_state.HasDomain(query.DomainName))
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{query.DomainName}' does not exist."));
            return;
        }

        var config = _state.Domains[query.DomainName];
        var upstreams = _state.Upstreams.TryGetValue(query.DomainName, out var list)
            ? list
            : (IReadOnlyList<UpstreamTarget>)[];

        Sender.Tell(new DomainConfigResult(config, upstreams));
    }

    private void Handle(GetSettings _)
    {
        Sender.Tell(_state.Settings);
    }

    private void PublishClusterEvent(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Done.Instance,
            failure: ex => new PublishFailed(ex));
    }

    private void PersistAndApply<TEvent>(TEvent evt, Action afterPersist) where TEvent : notnull
    {
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            afterPersist();
            SaveSnapshotIfNeeded();
        });
    }

    private void ApplyEvent(object evt)
    {
        switch (evt)
        {
            case DomainAdded e: _state.Apply(e); break;
            case DomainUpdated e: _state.Apply(e); break;
            case DomainRemoved e: _state.Apply(e); break;
            case UpstreamAdded e: _state.Apply(e); break;
            case UpstreamRemoved e: _state.Apply(e); break;
            case SettingsUpdated e: _state.Apply(e); break;
        }
    }

    private void SaveSnapshotIfNeeded()
    {
        if (_snapshotInterval > 0 && _state.EventCount % _snapshotInterval == 0)
        {
            SaveSnapshot(_state.ToSnapshot());
            _log.Info("Snapshot saved at event count {Count}", _state.EventCount);
        }
    }

    private sealed record PublishDropped(IClusterEvent Event);

    private sealed record PublishFailed(Exception Exception);
}