using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Routing;
using Xunit;
using EventHub = Schleusenwerk.Persistence.EventHub;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainEntityActorSpec : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;

    public DomainEntityActorSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private (IActorRef entity, TestProbe upstreamRegion) CreateEntity()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var upstreamRegion = CreateTestProbe();
        var entity = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamRegion)),
            $"entity-{Guid.NewGuid():N}");
        return (entity, upstreamRegion);
    }

    private static SetRoute MakeRoute(string domain, params string[] upstreams)
    {
        var config = new DomainConfig { DomainName = DomainName.Parse(domain) };
        var targets = upstreams.Select(url => UpstreamTarget.Create(url)).ToList();
        return new SetRoute(config, targets);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_stash_ResolveUpstream_until_SetRoute_arrives()
    {
        var (entity, upstreamRegion) = CreateEntity();

        entity.Tell(new ResolveUpstream("example.com"));
        entity.Tell(MakeRoute("example.com", "http://a:8080"));

        // After SetRoute, the stashed ResolveUpstream is processed
        upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout); // Drain RegisterUpstream
        var forwarded = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Equal("example.com", forwarded.Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_forward_SelectUpstreamForDomain_to_upstream_region()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));
        upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout); // Drain RegisterUpstream

        entity.Tell(new ResolveUpstream("example.com"));

        var forwarded = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Equal("example.com", forwarded.Config.DomainName.Value);
        Assert.Contains("a", forwarded.Url);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_send_RegisterUpstream_per_upstream_on_SetRoute()
    {
        var (entity, upstreamRegion) = CreateEntity();

        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));

        var msgs = new List<RegisterUpstream>
        {
            upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout),
            upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout)
        };
        var urls = msgs.Select(m => m.Target.Url.Host).ToHashSet();
        Assert.Contains("a", urls);
        Assert.Contains("b", urls);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_round_robin_across_upstreams()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090", "http://c:7070"));
        // Drain RegisterUpstream messages
        upstreamRegion.ReceiveN(3, Timeout);

        var hosts = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            hosts.Add(new Uri(fwd.Url).Host);
        }

        Assert.Equal(2, hosts.Count(h => h == "a"));
        Assert.Equal(2, hosts.Count(h => h == "b"));
        Assert.Equal(2, hosts.Count(h => h == "c"));
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reply_UpstreamNotFound_when_no_upstreams_in_route()
    {
        var (entity, _) = CreateEntity();
        entity.Tell(MakeRoute("example.com")); // empty upstream list

        var result = await entity.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_publish_RoutesUpdated_on_SetRoute_when_Ready()
    {
        var tcs = new TaskCompletionSource<RoutesUpdated>();
        var subscriber = Sys.ActorOf(Props.Create(() => new TcsActor<RoutesUpdated>(tcs)));
        Sys.EventStream.Subscribe(subscriber, typeof(RoutesUpdated));

        var (entity, _) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080")); // first SetRoute triggers Become(Ready)
        entity.Tell(MakeRoute("example.com", "http://b:9090")); // second SetRoute in Ready publishes event

        var evt = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Single(evt.Domains);
        Assert.Equal("example.com", evt.Domains[0].Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_publish_RouteRemoved_on_RemoveDomain()
    {
        var tcs = new TaskCompletionSource<RouteRemoved>();
        var subscriber = Sys.ActorOf(Props.Create(() => new TcsActor<RouteRemoved>(tcs)));
        Sys.EventStream.Subscribe(subscriber, typeof(RouteRemoved));

        var (entity, _) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));
        entity.Tell(new RemoveDomain(DomainName.Parse("example.com")));

        var evt = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("example.com", evt.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_update_config_on_second_SetRoute()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://old:8080"));
        upstreamRegion.ReceiveN(1, Timeout);

        entity.Tell(MakeRoute("example.com", "http://new:9090"));
        upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout);

        entity.Tell(new ResolveUpstream("example.com"));
        var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Contains("new", fwd.Url);
    }

    private sealed class TcsActor<T> : ReceiveActor
    {
        public TcsActor(TaskCompletionSource<T> tcs)
        {
            Receive<T>(tcs.TrySetResult);
        }
    }
}
