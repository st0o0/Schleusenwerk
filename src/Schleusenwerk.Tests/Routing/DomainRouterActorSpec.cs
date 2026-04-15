using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainRouterActorSpec : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;

    public DomainRouterActorSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private IActorRef CreateRouter()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var router = Sys.ActorOf(Props.Create<DomainRouterActor>(), $"router-{Guid.NewGuid():N}");
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
    public async Task ResolveUpstream_should_return_route_for_exact_match()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Route.DomainName.Value);
        Assert.Single(result.Route.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_match_case_insensitively()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("Example.COM"), Timeout);

        Assert.Equal("example.com", result.Route.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_return_not_found_for_unknown_host()
    {
        var router = CreateRouter();

        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("unknown.com"), Timeout);

        Assert.Equal("unknown.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_return_route_for_wildcard_match()
    {
        var router = CreateRouter();
        var route = CreateRoute("*.example.com", "http://localhost:9090");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("api.example.com"), Timeout);

        Assert.Equal("*.example.com", result.Route.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_not_match_apex_domain_with_wildcard()
    {
        var router = CreateRouter();
        var route = CreateRoute("*.example.com", "http://localhost:9090");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_prefer_exact_match_over_wildcard()
    {
        var router = CreateRouter();
        var exactRoute = CreateRoute("api.example.com", "http://exact:8080");
        var wildcardRoute = CreateRoute("*.example.com", "http://wildcard:9090");

        router.Tell(new UpdateRoutes([exactRoute, wildcardRoute]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("api.example.com"), Timeout);

        Assert.Equal("api.example.com", result.Route.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateRoutes_should_overwrite_existing_route()
    {
        var router = CreateRouter();
        var original = CreateRoute("example.com", "http://old:8080");
        var updated = CreateRoute("example.com", "http://new:9090");

        router.Tell(new UpdateRoutes([original]));
        router.Tell(new UpdateRoutes([updated]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("http://new:9090/", result.Route.Upstreams[0].Url.Value.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateRoutes_should_make_all_routes_resolvable()
    {
        var router = CreateRouter();
        var routeA = CreateRoute("a.com", "http://a:8080");
        var routeB = CreateRoute("b.com", "http://b:9090");

        router.Tell(new UpdateRoutes([routeA, routeB]));

        var resultA = await router.Ask<UpstreamResolved>(new ResolveUpstream("a.com"), Timeout);
        var resultB = await router.Ask<UpstreamResolved>(new ResolveUpstream("b.com"), Timeout);

        Assert.Equal("a.com", resultA.Route.DomainName.Value);
        Assert.Equal("b.com", resultB.Route.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomain_should_remove_existing_route()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));
        router.Tell(new Schleusenwerk.Routing.RemoveDomain(DomainName.Parse("example.com")));

        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomain_should_not_throw_for_nonexistent_domain()
    {
        var router = CreateRouter();

        // Should not crash the actor
        router.Tell(new Schleusenwerk.Routing.RemoveDomain(DomainName.Parse("nonexistent.com")));

        // Actor is still alive — verify by asking it
        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("nonexistent.com"), Timeout,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("nonexistent.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateRoutes_should_publish_routes_updated_event()
    {
        var tcs = new TaskCompletionSource<RoutesUpdated>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventSubscriberActor<RoutesUpdated>(tcs)));
        Sys.EventStream.Subscribe(subscriber, typeof(RoutesUpdated));

        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));

        var evt = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Single(evt.Domains);
        Assert.Equal("example.com", evt.Domains[0].Value);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomain_should_publish_route_removed_event()
    {
        var tcs = new TaskCompletionSource<RouteRemoved>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventSubscriberActor<RouteRemoved>(tcs)));
        Sys.EventStream.Subscribe(subscriber, typeof(RouteRemoved));

        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));
        router.Tell(new Schleusenwerk.Routing.RemoveDomain(DomainName.Parse("example.com")));

        var evt = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("example.com", evt.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomain_should_not_publish_event_for_nonexistent_domain()
    {
        var tcs = new TaskCompletionSource<RouteRemoved>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventSubscriberActor<RouteRemoved>(tcs)));
        Sys.EventStream.Subscribe(subscriber, typeof(RouteRemoved));

        var router = CreateRouter();
        router.Tell(new Schleusenwerk.Routing.RemoveDomain(DomainName.Parse("nonexistent.com")));

        // Ensure actor has processed the message by asking it something
        await router.Ask<UpstreamNotFound>(new ResolveUpstream("nonexistent.com"), Timeout,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(tcs.Task.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_return_all_upstreams()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://a:8080", "http://b:9090", "http://c:7070");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Route.Upstreams.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainRouterActor_should_filter_unhealthy_upstream_received_via_hub()
    {
        var router = CreateRouter();
        var hub = await _registry.GetAsync<EventHub>(TestContext.Current.CancellationToken);

        var route = CreateRoute("health.com", "http://healthy:8080", "http://sick:9090");

        router.Tell(new UpdateRoutes([route]));
        // Allow subscription handshake to complete
        await Task.Delay(150);

        hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://sick:9090"), IsHealthy: false));
        await Task.Delay(100);

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("health.com"), Timeout);

        Assert.Single(result.Route.Upstreams);
        Assert.Equal("http://healthy:8080/", result.Route.Upstreams[0].Url.Value.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task DomainRouterActor_should_restore_upstream_after_healthy_event_via_hub()
    {
        var router = CreateRouter();
        var hub = await _registry.GetAsync<EventHub>(TestContext.Current.CancellationToken);

        var route = CreateRoute("recover.com", "http://a:8080", "http://b:9090");

        router.Tell(new UpdateRoutes([route]));
        await Task.Delay(150);

        hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://b:9090"), IsHealthy: false));
        await Task.Delay(100);
        hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://b:9090"), IsHealthy: true));
        await Task.Delay(100);

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("recover.com"), Timeout);

        Assert.Equal(2, result.Route.Upstreams.Count);
    }

    private sealed class EventSubscriberActor<T> : ReceiveActor
    {
        public EventSubscriberActor(TaskCompletionSource<T> tcs)
        {
            Receive<T>(tcs.TrySetResult);
        }
    }
}