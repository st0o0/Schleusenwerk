using Akka.Actor;
using Akka.Event;

namespace Schleusenwerk.Domain.Routing;

/// <summary>
/// Manages the routing table and resolves incoming host headers to upstream targets.
/// Thread-safe via actor model — all state mutations happen inside the actor's mailbox.
/// </summary>
public sealed class DomainRouterActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Dictionary<DomainName, RouteDefinition> _routes = new();

    public DomainRouterActor()
    {
        Receive<UpdateRoutes>(Handle);
        Receive<ResolveUpstream>(Handle);
        Receive<RemoveDomain>(Handle);
    }

    public static Props Props() => Akka.Actor.Props.Create(() => new DomainRouterActor());

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
            Sender.Tell(new UpstreamResolved(route));
            return;
        }

        // Try wildcard match — iterate wildcard entries only
        foreach (var kvp in _routes)
        {
            if (kvp.Key.IsWildcard && kvp.Key.Matches(host))
            {
                Sender.Tell(new UpstreamResolved(kvp.Value));
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
}
