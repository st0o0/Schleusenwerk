using Akka.Actor;
using Akka.Routing;
using Schleusenwerk.Routing;

namespace Schleusenwerk.LoadBalancing;

/// <summary>
/// Distributes requests across healthy upstreams using Akka.NET's RoundRobinGroup router.
/// Weight is honored by creating proportional routee actors per upstream.
/// Unhealthy upstreams are excluded by rebuilding the router.
/// </summary>
public sealed class LoadBalancerActor : ReceiveActor
{
    private IReadOnlyList<UpstreamTarget> _allUpstreams;
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private IActorRef? _router;
    private int _generation;

    public LoadBalancerActor(IReadOnlyList<UpstreamTarget> initialUpstreams)
    {
        _allUpstreams = initialUpstreams;
        RebuildRouter();

        Receive<SelectUpstream>(HandleSelect);
        Receive<MarkUpstreamUnhealthy>(HandleMarkUnhealthy);
        Receive<MarkUpstreamHealthy>(HandleMarkHealthy);
        Receive<UpdateUpstreams>(HandleUpdateUpstreams);
    }

    private void HandleSelect(SelectUpstream msg)
    {
        if (_router is null)
        {
            Sender.Tell(NoHealthyUpstreamAvailable.Instance);
            return;
        }

        _router.Forward(msg);
    }

    private void HandleMarkUnhealthy(MarkUpstreamUnhealthy msg)
    {
        if (_unhealthyUrls.Add(msg.Url))
            RebuildRouter();
    }

    private void HandleMarkHealthy(MarkUpstreamHealthy msg)
    {
        if (_unhealthyUrls.Remove(msg.Url))
            RebuildRouter();
    }

    private void HandleUpdateUpstreams(UpdateUpstreams msg)
    {
        _allUpstreams = msg.Targets;
        _unhealthyUrls.Clear();
        RebuildRouter();
    }

    private void RebuildRouter()
    {
        // Stop all existing children (previous routees and router)
        foreach (var child in Context.GetChildren())
        {
            Context.Stop(child);
        }

        _router = null;

        var healthyUpstreams = _allUpstreams
            .Where(u => !_unhealthyUrls.Contains(u.Url))
            .ToList();

        if (healthyUpstreams.Count == 0)
            return;

        var gen = ++_generation;
        var routeePaths = new List<string>();
        var index = 0;

        foreach (var upstream in healthyUpstreams)
        {
            for (var i = 0; i < upstream.Weight; i++)
            {
                var child = Context.ActorOf(Props.Create(() => new UpstreamRouteeActor(upstream)),
                    $"routee-g{gen}-{index++}");
                routeePaths.Add(child.Path.ToString());
            }
        }

        _router = Context.ActorOf(
            Props.Empty.WithRouter(new RoundRobinGroup(routeePaths)),
            $"router-g{gen}");
    }
}