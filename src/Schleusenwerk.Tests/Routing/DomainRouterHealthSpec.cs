using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.LoadBalancing;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainRouterHealthSpec : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private IActorRef _currentHub = null!;
    private readonly ActorRegistry _registry;

    public DomainRouterHealthSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private IActorRef CreateRouter()
    {
        _currentHub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(_currentHub, overwrite: true);

        var router = Sys.ActorOf(
            Props.Create(() => new DomainRouterActor(
                upstreams => Props.Create(() => new LoadBalancerActor(upstreams)))),
            $"router-{Guid.NewGuid():N}");
        _registry.Register<DomainRouterActor>(router, overwrite: true);

        return router;
    }

    private static RouteDefinition CreateRoute(string domain, params string[] upstreams)
    {
        var config = new DomainConfig { DomainName = DomainName.Parse(domain) };
        var targets = upstreams.Select(u => UpstreamTarget.Create(u)).ToList();
        return RouteDefinition.Create(config, targets);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_exclude_unhealthy_upstream()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://a:8080", "http://b:9090");

        router.Tell(new UpdateRoutes([route]));

        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        for (var i = 0; i < 3; i++)
        {
            var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);
            Assert.Equal("b", result.Target.Url.Host);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_return_not_found_when_all_upstreams_unhealthy()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://a:8080");

        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);
        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_restore_upstream_when_healthy_again()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://a:8080", "http://b:9090");

        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);
        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: true));
        await Task.Delay(150);

        var hosts = new HashSet<string>();
        for (var i = 0; i < 6; i++)
        {
            var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);
            hosts.Add(result.Target.Url.Host);
        }

        Assert.Contains("a", hosts);
        Assert.Contains("b", hosts);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_not_affect_unrelated_domains()
    {
        var router = CreateRouter();
        var routeA = CreateRoute("a.com", "http://a:8080");
        var routeB = CreateRoute("b.com", "http://b:9090");

        router.Tell(new UpdateRoutes([routeA, routeB]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("a.com"), Timeout);

        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        var resultB = await router.Ask<UpstreamResolved>(new ResolveUpstream("b.com"), Timeout);
        Assert.Equal("b", resultB.Target.Url.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_filter_unhealthy_from_wildcard_routes()
    {
        var router = CreateRouter();
        var route = CreateRoute("*.example.com", "http://a:8080", "http://b:9090");

        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("api.example.com"), Timeout);

        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://b:9090"), IsHealthy: false));
        await Task.Delay(150);

        for (var i = 0; i < 3; i++)
        {
            var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("api.example.com"), Timeout);
            Assert.Equal("a", result.Target.Url.Host);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_ignore_duplicate_unhealthy_events()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://a:8080", "http://b:9090");

        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        for (var i = 0; i < 3; i++)
        {
            var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);
            Assert.Equal("b", result.Target.Url.Host);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_ignore_healthy_event_for_unknown_upstream()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://a:8080");

        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        _currentHub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://unknown:9999"), IsHealthy: true));
        await Task.Delay(150);

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);
        Assert.Equal("a", result.Target.Url.Host);
    }
}
