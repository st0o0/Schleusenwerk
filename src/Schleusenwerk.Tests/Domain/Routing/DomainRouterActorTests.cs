using Akka.Actor;
using Schleusenwerk.Domain.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Domain.Routing;

public sealed class DomainRouterActorTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ActorSystem _sys = ActorSystem.Create("test");

    private IActorRef CreateRouter() => _sys.ActorOf(DomainRouterActor.Props(), $"router-{Guid.NewGuid():N}");

    public async ValueTask DisposeAsync() => await _sys.Terminate();

    private static RouteDefinition CreateRoute(string domain, params string[] upstreams)
    {
        var config = new DomainConfig { DomainName = DomainName.Parse(domain) };
        var targets = upstreams.Select(u => UpstreamTarget.Create(u)).ToList();
        return RouteDefinition.Create(config, targets);
    }

    [Fact]
    public async Task ResolveUpstream_ExactMatch_ReturnsRoute()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Route.DomainName.Value);
        Assert.Single(result.Route.Upstreams);
    }

    [Fact]
    public async Task ResolveUpstream_CaseInsensitive_ReturnsRoute()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("Example.COM"), Timeout);

        Assert.Equal("example.com", result.Route.DomainName.Value);
    }

    [Fact]
    public async Task ResolveUpstream_UnknownHost_ReturnsNotFound()
    {
        var router = CreateRouter();

        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("unknown.com"), Timeout);

        Assert.Equal("unknown.com", result.Host);
    }

    [Fact]
    public async Task ResolveUpstream_WildcardMatch_ReturnsRoute()
    {
        var router = CreateRouter();
        var route = CreateRoute("*.example.com", "http://localhost:9090");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("api.example.com"), Timeout);

        Assert.Equal("*.example.com", result.Route.DomainName.Value);
    }

    [Fact]
    public async Task ResolveUpstream_WildcardDoesNotMatchApex_ReturnsNotFound()
    {
        var router = CreateRouter();
        var route = CreateRoute("*.example.com", "http://localhost:9090");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact]
    public async Task ResolveUpstream_ExactMatchTakesPrecedenceOverWildcard()
    {
        var router = CreateRouter();
        var exactRoute = CreateRoute("api.example.com", "http://exact:8080");
        var wildcardRoute = CreateRoute("*.example.com", "http://wildcard:9090");

        router.Tell(new UpdateRoutes([exactRoute, wildcardRoute]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("api.example.com"), Timeout);

        Assert.Equal("api.example.com", result.Route.DomainName.Value);
    }

    [Fact]
    public async Task UpdateRoutes_OverwritesExistingRoute()
    {
        var router = CreateRouter();
        var original = CreateRoute("example.com", "http://old:8080");
        var updated = CreateRoute("example.com", "http://new:9090");

        router.Tell(new UpdateRoutes([original]));
        router.Tell(new UpdateRoutes([updated]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("http://new:9090/", result.Route.Upstreams[0].Url.Value.ToString());
    }

    [Fact]
    public async Task UpdateRoutes_MultipleRoutes_AllResolvable()
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

    [Fact]
    public async Task RemoveDomain_ExistingDomain_RemovesRoute()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));
        router.Tell(new RemoveDomain(DomainName.Parse("example.com")));

        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact]
    public async Task RemoveDomain_NonExistentDomain_DoesNotThrow()
    {
        var router = CreateRouter();

        // Should not crash the actor
        router.Tell(new RemoveDomain(DomainName.Parse("nonexistent.com")));

        // Actor is still alive — verify by asking it
        var result = await router.Ask<UpstreamNotFound>(new ResolveUpstream("nonexistent.com"), Timeout);
        Assert.Equal("nonexistent.com", result.Host);
    }

    [Fact]
    public async Task UpdateRoutes_PublishesRoutesUpdatedEvent()
    {
        var tcs = new TaskCompletionSource<RoutesUpdated>();
        var subscriber = _sys.ActorOf(Props.Create(() => new EventSubscriberActor<RoutesUpdated>(tcs)));
        _sys.EventStream.Subscribe(subscriber, typeof(RoutesUpdated));

        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));

        var evt = await tcs.Task.WaitAsync(Timeout);
        Assert.Single(evt.Domains);
        Assert.Equal("example.com", evt.Domains[0].Value);
    }

    [Fact]
    public async Task RemoveDomain_PublishesRouteRemovedEvent()
    {
        var tcs = new TaskCompletionSource<RouteRemoved>();
        var subscriber = _sys.ActorOf(Props.Create(() => new EventSubscriberActor<RouteRemoved>(tcs)));
        _sys.EventStream.Subscribe(subscriber, typeof(RouteRemoved));

        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://localhost:8080");

        router.Tell(new UpdateRoutes([route]));
        router.Tell(new RemoveDomain(DomainName.Parse("example.com")));

        var evt = await tcs.Task.WaitAsync(Timeout);
        Assert.Equal("example.com", evt.DomainName.Value);
    }

    [Fact]
    public async Task RemoveDomain_NonExistent_DoesNotPublishEvent()
    {
        var tcs = new TaskCompletionSource<RouteRemoved>();
        var subscriber = _sys.ActorOf(Props.Create(() => new EventSubscriberActor<RouteRemoved>(tcs)));
        _sys.EventStream.Subscribe(subscriber, typeof(RouteRemoved));

        var router = CreateRouter();
        router.Tell(new RemoveDomain(DomainName.Parse("nonexistent.com")));

        // Ensure actor has processed the message by asking it something
        await router.Ask<UpstreamNotFound>(new ResolveUpstream("nonexistent.com"), Timeout);

        Assert.False(tcs.Task.IsCompleted);
    }

    [Fact]
    public async Task ResolveUpstream_MultipleUpstreams_ReturnsAll()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", "http://a:8080", "http://b:9090", "http://c:7070");

        router.Tell(new UpdateRoutes([route]));

        var result = await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal(3, result.Route.Upstreams.Count);
    }

    /// <summary>
    /// Simple actor that captures the first message of type T into a TaskCompletionSource.
    /// </summary>
    private sealed class EventSubscriberActor<T> : ReceiveActor
    {
        public EventSubscriberActor(TaskCompletionSource<T> tcs)
        {
            Receive<T>(msg => tcs.TrySetResult(msg));
        }
    }
}
