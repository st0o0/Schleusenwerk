using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class UpstreamEntityActorSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private int _actorCounter;

    private IActorRef CreateEntity(IHealthCheckPropsFactory? factory = null)
    {
        var id = Interlocked.Increment(ref _actorCounter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        factory ??= new TestHealthCheckPropsFactory(_ => Props.Create<NullActor>());

        return Sys.ActorOf(
            Props.Create(() => new UpstreamEntityActor(factory)),
            $"upstream-{id:D4}");
    }

    [Fact(Timeout = 5000)]
    public async Task UpstreamEntityActor_should_persist_target_on_RegisterUpstream()
    {
        var target = UpstreamTarget.Create("http://upstream:8080");
        var entity = CreateEntity();

        var ack = await entity.Ask<ConfigurationCommandAck>(new RegisterUpstream(target), Timeout);

        Assert.NotNull(ack);
    }

    [Fact(Timeout = 5000)]
    public async Task UpstreamEntityActor_should_reply_UpstreamResolved_after_register()
    {
        var target = UpstreamTarget.Create("http://upstream:8080");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var entity = CreateEntity();

        await entity.Ask<ConfigurationCommandAck>(new RegisterUpstream(target), Timeout);
        var resolved = await entity.Ask<UpstreamResolved>(
            new SelectUpstreamForDomain(config, "http://upstream:8080/"), Timeout);

        Assert.Equal("upstream", resolved.Target.Url.Host);
        Assert.Equal("example.com", resolved.Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task UpstreamEntityActor_should_update_target_on_new_RegisterUpstream()
    {
        var first = UpstreamTarget.Create("http://v1:8080");
        var second = UpstreamTarget.Create("http://v2:9090");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var entity = CreateEntity();

        await entity.Ask<ConfigurationCommandAck>(new RegisterUpstream(first), Timeout);
        await entity.Ask<ConfigurationCommandAck>(new RegisterUpstream(second), Timeout);

        var resolved = await entity.Ask<UpstreamResolved>(
            new SelectUpstreamForDomain(config, "http://v2:9090/"), Timeout);

        Assert.Equal("v2", resolved.Target.Url.Host);
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_reply_UpstreamNotFound_when_no_target()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var entity = CreateEntity();

        entity.Tell(new SelectUpstreamForDomain(config, "http://any:8080/"));

        ExpectMsg<UpstreamNotFound>(Timeout);
    }

    private sealed class TestHealthCheckPropsFactory(Func<UpstreamTarget, Props> factory) : IHealthCheckPropsFactory
    {
        public Props CreateProps(UpstreamTarget target) => factory(target);
    }

    private sealed class NullActor : ReceiveActor { }
}
