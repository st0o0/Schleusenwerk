using Akka.Actor;
using Akka.Routing;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Routing;

namespace Schleusenwerk.LoadBalancing;

/// <summary>
/// Distributes requests across healthy upstreams using Akka.NET's RoundRobinGroup router.
/// Weight is honored by creating proportional routee actors per upstream.
/// Unhealthy upstreams are excluded by rebuilding the router.
/// Manages one HealthCheckActor child per upstream when a healthCheckPropsFactory is provided.
/// </summary>
public sealed class LoadBalancerActor : ReceiveActor
{
    private IReadOnlyList<UpstreamTarget> _allUpstreams;
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private readonly Func<UpstreamTarget, Props>? _healthCheckPropsFactory;
    private readonly Dictionary<UpstreamUrl, IActorRef> _healthCheckActors = new();
    private readonly HashSet<IActorRef> _routeeActors = [];
    private IActorRef? _router;
    private int _generation;

    public LoadBalancerActor(
        IReadOnlyList<UpstreamTarget> initialUpstreams,
        Func<UpstreamTarget, Props>? healthCheckPropsFactory = null)
    {
        _allUpstreams = initialUpstreams;
        _healthCheckPropsFactory = healthCheckPropsFactory;

        RebuildRouter();
        SyncHealthCheckActors(_allUpstreams);

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
        {
            RebuildRouter();
        }
    }

    private void HandleMarkHealthy(MarkUpstreamHealthy msg)
    {
        if (_unhealthyUrls.Remove(msg.Url))
        {
            RebuildRouter();
        }
    }

    private void HandleUpdateUpstreams(UpdateUpstreams msg)
    {
        _allUpstreams = msg.Targets;
        _unhealthyUrls.Clear();
        RebuildRouter();
        SyncHealthCheckActors(msg.Targets);
    }

    private void RebuildRouter()
    {
        // Stop only routee actors and the router — leave health check actors running
        if (_router is not null)
        {
            Context.Stop(_router);
            _router = null;
        }

        foreach (var routee in _routeeActors)
        {
            Context.Stop(routee);
        }

        _routeeActors.Clear();

        var healthyUpstreams = _allUpstreams
            .Where(u => !_unhealthyUrls.Contains(u.Url))
            .ToList();

        if (healthyUpstreams.Count == 0)
        {
            return;
        }

        var gen = ++_generation;
        var routeePaths = new List<string>();
        var index = 0;

        foreach (var upstream in healthyUpstreams)
        {
            for (var i = 0; i < upstream.Weight; i++)
            {
                var child = Context.ActorOf(
                    Props.Create(() => new UpstreamRouteeActor(upstream)),
                    $"routee-g{gen}-{index++}");
                _routeeActors.Add(child);
                routeePaths.Add(child.Path.ToString());
            }
        }

        _router = Context.ActorOf(
            Props.Empty.WithRouter(new RoundRobinGroup(routeePaths)),
            $"router-g{gen}");
    }

    private void SyncHealthCheckActors(IReadOnlyList<UpstreamTarget> upstreams)
    {
        if (_healthCheckPropsFactory is null)
        {
            return;
        }

        var newUrls = upstreams.Select(u => u.Url).ToHashSet();

        // Stop health checkers for removed upstreams
        foreach (var url in _healthCheckActors.Keys.Where(u => !newUrls.Contains(u)).ToList())
        {
            Context.Stop(_healthCheckActors[url]);
            _healthCheckActors.Remove(url);
        }

        // Start health checkers for new upstreams
        foreach (var upstream in upstreams)
        {
            if (!_healthCheckActors.ContainsKey(upstream.Url))
            {
                var sanitized = SanitizeActorName(upstream.Url.ToString());
                var checker = Context.ActorOf(
                    _healthCheckPropsFactory(upstream),
                    $"health-{sanitized}");
                _healthCheckActors[upstream.Url] = checker;
            }
        }
    }

    private static string SanitizeActorName(string name)
    {
        return new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
    }
}
