using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Routing;

public sealed class DomainEntityActor : ReceiveActor, IWithUnboundedStash
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _upstreamRegion;
    private readonly IActorRef _eventHub;

    private DomainConfig _config = null!;
    private List<UpstreamUrl> _upstreams = [];
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private int _roundRobinIndex;

    public IStash Stash { get; set; } = null!;

    public DomainEntityActor(IActorRef upstreamRegion)
    {
        _upstreamRegion = upstreamRegion;
        _eventHub = Context.GetActor<EventHub>();
        WaitingForSubscription();
    }

    protected override void PreStart()
    {
        _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance)
            .PipeTo(Self);
    }

    private void WaitingForSubscription()
    {
        Receive<EventHub.Subscribed>(msg =>
        {
            var self = Self;
            msg.SourceRef.Source.RunForeach(evt => self.Tell(evt), Context.Materializer());
            Stash.UnstashAll();
            Become(WaitingForRoute);
        });
        Receive<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "DomainEntityActor failed to subscribe to EventHub — retrying");
            _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance)
                .PipeTo(Self);
        });
        ReceiveAny(_ => Stash.Stash());
    }

    private void WaitingForRoute()
    {
        Receive<SetRoute>(msg =>
        {
            ApplySetRoute(msg);
            Stash.UnstashAll();
            Become(Ready);
        });
        Receive<ResolveUpstream>(_ => Stash.Stash());
        ReceiveAny(_ => { });
    }

    private void Ready()
    {
        Receive<SetRoute>(msg =>
        {
            ApplySetRoute(msg);
            Context.System.EventStream.Publish(new RoutesUpdated([_config.DomainName]));
        });
        Receive<ResolveUpstream>(HandleResolveUpstream);
        Receive<RemoveDomain>(_ =>
        {
            Context.System.EventStream.Publish(new RouteRemoved(_config.DomainName));
            Self.Tell(PoisonPill.Instance);
        });
        Receive<UpstreamHealthChanged>(msg =>
        {
            if (msg.IsHealthy)
                _unhealthyUrls.Remove(msg.Url);
            else
                _unhealthyUrls.Add(msg.Url);
        });
    }

    private void ApplySetRoute(SetRoute msg)
    {
        _config = msg.Config;
        _upstreams = msg.Upstreams.Select(u => u.Url).ToList();
        _unhealthyUrls.IntersectWith(_upstreams.ToHashSet());
        _roundRobinIndex = 0;
        foreach (var upstream in msg.Upstreams)
        {
            _upstreamRegion.Tell(new RegisterUpstream(upstream));
        }
    }

    private void HandleResolveUpstream(ResolveUpstream msg)
    {
        var healthy = _upstreams.Where(u => !_unhealthyUrls.Contains(u)).ToList();
        if (healthy.Count == 0)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }
        var picked = healthy[_roundRobinIndex % healthy.Count];
        _roundRobinIndex++;
        _upstreamRegion.Tell(new SelectUpstreamForDomain(_config, picked.Value.ToString()), Sender);
    }
}
