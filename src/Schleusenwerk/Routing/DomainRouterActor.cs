using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Streams;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Routing;

/// <summary>
/// Manages the routing table and resolves incoming host headers to upstream targets.
/// Subscribes to IClusterEvent via EventHubActor to receive UpstreamHealthChanged events.
/// Thread-safe via actor model — all state mutations happen inside the actor's mailbox.
/// </summary>
public sealed class DomainRouterActor : ReceiveActor, IWithUnboundedStash
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _eventHub;
    private readonly Dictionary<DomainName, RouteDefinition> _routes = new();
    private readonly HashSet<UpstreamUrl> _unhealthyUpstreams = [];
    public IStash Stash { get; set; } = null!;

    public DomainRouterActor()
    {
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
            _log.Info("Route updated for domain {Domain} with {Count} upstream(s)",
                route.DomainName, route.Upstreams.Count);
        }

        Context.System.EventStream.Publish(new RoutesUpdated(updatedDomains));
    }

    private void Handle(ResolveUpstream msg)
    {
        var host = msg.Host;

        // Try exact match first (O(1) dictionary lookup)
        if (DomainName.TryParse(host, out var domainName) && _routes.TryGetValue(domainName, out var route))
        {
            var filtered = FilterHealthyUpstreams(route);
            if (filtered is not null)
            {
                Sender.Tell(new UpstreamResolved(filtered));
                return;
            }

            Sender.Tell(new UpstreamNotFound(host));
            return;
        }

        // Try wildcard match — iterate wildcard entries only
        foreach (var kvp in _routes)
        {
            if (kvp.Key.IsWildcard && kvp.Key.Matches(host))
            {
                var filtered = FilterHealthyUpstreams(kvp.Value);
                if (filtered is not null)
                {
                    Sender.Tell(new UpstreamResolved(filtered));
                    return;
                }

                Sender.Tell(new UpstreamNotFound(host));
                return;
            }
        }

        Sender.Tell(new UpstreamNotFound(host));
    }

    private void Handle(RemoveDomain msg)
    {
        if (_routes.Remove(msg.DomainName))
        {
            _log.Info("Route removed for domain {Domain}", msg.DomainName);
            Context.System.EventStream.Publish(new RouteRemoved(msg.DomainName));
        }
    }

    private void Handle(UpstreamHealthChanged msg)
    {
        if (msg.IsHealthy)
        {
            if (_unhealthyUpstreams.Remove(msg.Url))
            {
                _log.Info("Upstream {Url} marked healthy", msg.Url);
            }
        }
        else
        {
            if (_unhealthyUpstreams.Add(msg.Url))
            {
                _log.Warning("Upstream {Url} marked unhealthy", msg.Url);
            }
        }
    }

    private RouteDefinition? FilterHealthyUpstreams(RouteDefinition route)
    {
        if (_unhealthyUpstreams.Count == 0)
        {
            return route;
        }

        var healthyUpstreams = route.Upstreams
            .Where(u => !_unhealthyUpstreams.Contains(u.Url))
            .ToList();

        if (healthyUpstreams.Count == 0)
        {
            return null;
        }

        if (healthyUpstreams.Count == route.Upstreams.Count)
        {
            return route;
        }

        return RouteDefinition.Create(route.Config, healthyUpstreams);
    }
}