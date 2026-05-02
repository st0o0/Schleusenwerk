namespace Schleusenwerk.Tests.Routing;

/// <summary>
/// TODO: Task 6+ — Update to use DomainEntityActor query messages instead of ConfigurationPersistenceActor.
/// Tests expect GetDomainByName to be answered by ConfigurationPersistenceActor, but that actor
/// no longer handles domain/upstream commands. DomainEntityActor should answer GetDomainConfig instead.
/// </summary>
#if false
public sealed class DomainEntityHealthSpec : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;
    private IActorRef _hub = null!;

    public DomainEntityHealthSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private (IActorRef entity, TestProbe upstreamRegion) CreateEntity()
    {
        _hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(_hub, overwrite: true);

        var upstreamRegion = CreateTestProbe();
        _registry.Register<UpstreamEntityActor>(upstreamRegion, overwrite: true);

        var configProbe = CreateTestProbe();
        _registry.Register<ConfigurationPersistenceActor>(configProbe, overwrite: true);

        var entity = Sys.ActorOf(
            Props.Create<DomainEntityActor>(),
            $"entity-{Guid.NewGuid():N}");

        // Answer the initial config query
        configProbe.ExpectMsg<GetDomainByName>(Timeout);
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        configProbe.Reply(new DomainConfigResult(config, []));

        return (entity, upstreamRegion);
    }

    private static SetRoute MakeRoute(string domain, params string[] upstreams)
    {
        var config = new DomainConfig { DomainName = DomainName.Parse(domain) };
        var targets = upstreams.Select(url => UpstreamTarget.Create(url)).ToList();
        return new SetRoute(config, targets);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_exclude_unhealthy_upstream_received_via_EventHub()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));
        upstreamRegion.ReceiveN(2, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        for (var i = 0; i < 3; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            Assert.Contains("b", fwd.Url);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_return_UpstreamNotFound_when_all_unhealthy()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));
        upstreamRegion.ReceiveN(1, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        var result = await entity.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);
        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_restore_upstream_after_healthy_event()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));
        upstreamRegion.ReceiveN(2, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);
        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: true));
        await Task.Delay(150);

        var hosts = new HashSet<string>();
        for (var i = 0; i < 6; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            hosts.Add(new Uri(fwd.Url).Host);
        }

        Assert.Contains("a", hosts);
        Assert.Contains("b", hosts);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_ignore_duplicate_unhealthy_events()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));
        upstreamRegion.ReceiveN(2, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        for (var i = 0; i < 3; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            Assert.Contains("b", fwd.Url);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_ignore_healthy_event_for_unknown_upstream()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));
        upstreamRegion.ReceiveN(1, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://unknown:9999"), IsHealthy: true));
        await Task.Delay(100);

        entity.Tell(new ResolveUpstream("example.com"));
        var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Contains("a", fwd.Url);
    }
}
#endif
