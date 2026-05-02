using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.LoadBalancing;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Routing;

/// <summary>
/// Manages the routing table and resolves incoming host headers to upstream targets.
/// Spawns one LoadBalancerActor child per domain. Delegates upstream selection to the child.
/// Subscribes to EventHub to receive UpstreamHealthChanged events and forwards them to children.
/// </summary>
public sealed class DomainRouterActor : ReceiveActor, IWithUnboundedStash
{
    private static readonly TimeSpan SelectTimeout = TimeSpan.FromSeconds(5);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _eventHub;
    private readonly Func<IReadOnlyList<UpstreamTarget>, Props> _loadBalancerPropsFactory;
    private readonly Dictionary<DomainName, RouteDefinition> _routes = new();
    private readonly Dictionary<DomainName, IActorRef> _loadBalancers = new();
    private readonly HashSet<UpstreamUrl> _unhealthyUpstreams = [];
    public IStash Stash { get; set; } = null!;

    public DomainRouterActor(Func<IReadOnlyList<UpstreamTarget>, Props> loadBalancerPropsFactory)
    {
        _loadBalancerPropsFactory = loadBalancerPropsFactory;
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
            msg.SourceRef.Source
                .RunForeach(evt => self.Tell(evt), Context.Materializer());
            _log.Info("DomainRouterActor subscribed to EventHubActor");
            Become(Ready);
            Stash.UnstashAll();
        });
        Receive<Status.Failure>(f =>
        {
            _log.Error(f.Cause, "DomainRouterActor failed to subscribe to EventHubActor — retrying");
            _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance)
                .PipeTo(Self);
        });
        ReceiveAny(_ => Stash.Stash());
    }

    private void Ready()
    {
        Receive<UpdateRoutes>(Handle);
        Receive<ResolveUpstream>(Handle);
        Receive<RemoveDomain>(Handle);
        Receive<UpstreamHealthChanged>(Handle);
        Receive<IClusterEvent>(_ => { }); // Ignore other cluster events
    }

    private void Handle(UpdateRoutes msg)
    {
        var updatedDomains = new List<DomainName>(msg.Routes.Count);

        foreach (var route in msg.Routes)
        {
            _routes[route.DomainName] = route;
            updatedDomains.Add(route.DomainName);

            if (_loadBalancers.TryGetValue(route.DomainName, out var existing))
            {
                existing.Tell(new UpdateUpstreams(route.Upstreams));
            }
            else
            {
                var lb = Context.ActorOf(
                    _loadBalancerPropsFactory(route.Upstreams),
                    SanitizeActorName(route.DomainName.Value));
                _loadBalancers[route.DomainName] = lb;
            }

            _log.Info("Route updated for domain {Domain} with {Count} upstream(s)",
                route.DomainName, route.Upstreams.Count);
        }

        Context.System.EventStream.Publish(new RoutesUpdated(updatedDomains));
    }

    private void Handle(ResolveUpstream msg)
    {
        var host = msg.Host;
        var sender = Sender;

        if (DomainName.TryParse(host, out var domainName) && _loadBalancers.TryGetValue(domainName, out var lb))
        {
            var config = _routes[domainName].Config;
            lb.Ask<object>(SelectUpstream.Instance, SelectTimeout)
                .PipeTo(sender,
                    success: result => result switch
                    {
                        UpstreamSelected sel => (object)new UpstreamResolved(sel.Target, config),
                        _ => new UpstreamNotFound(host)
                    },
                    failure: _ => new UpstreamNotFound(host));
            return;
        }

        // Try wildcard match
        foreach (var kvp in _routes)
        {
            if (kvp.Key.IsWildcard && kvp.Key.Matches(host) && _loadBalancers.TryGetValue(kvp.Key, out var wlb))
            {
                var config = kvp.Value.Config;
                wlb.Ask<object>(SelectUpstream.Instance, SelectTimeout)
                    .PipeTo(sender,
                        success: result => result switch
                        {
                            UpstreamSelected sel => (object)new UpstreamResolved(sel.Target, config),
                            _ => new UpstreamNotFound(host)
                        },
                        failure: _ => new UpstreamNotFound(host));
                return;
            }
        }

        sender.Tell(new UpstreamNotFound(host));
    }

    private void Handle(RemoveDomain msg)
    {
        if (_routes.Remove(msg.DomainName))
        {
            if (_loadBalancers.Remove(msg.DomainName, out var lb))
            {
                Context.Stop(lb);
            }

            _log.Info("Route removed for domain {Domain}", msg.DomainName);
            Context.System.EventStream.Publish(new RouteRemoved(msg.DomainName));
        }
    }

    private void Handle(UpstreamHealthChanged msg)
    {
        if (msg.IsHealthy)
        {
            _unhealthyUpstreams.Remove(msg.Url);
            _log.Info("Upstream {Url} marked healthy", msg.Url);
        }
        else
        {
            _unhealthyUpstreams.Add(msg.Url);
            _log.Warning("Upstream {Url} marked unhealthy", msg.Url);
        }

        // Forward to each load balancer that owns this upstream
        foreach (var kvp in _routes)
        {
            if (kvp.Value.Upstreams.Any(u => u.Url == msg.Url) && _loadBalancers.TryGetValue(kvp.Key, out var lb))
            {
                if (msg.IsHealthy)
                {
                    lb.Tell(new MarkUpstreamHealthy(msg.Url));
                }
                else
                {
                    lb.Tell(new MarkUpstreamUnhealthy(msg.Url));
                }
            }
        }
    }

    private static string SanitizeActorName(string name)
    {
        return new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
    }
}
