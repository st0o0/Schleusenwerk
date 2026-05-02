using Akka.Actor;
using Akka.TestKit.Xunit;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class UpstreamEntityActorSpec : TestKit
{
    private IActorRef CreateEntity(IActorRef? healthCheckProbe = null)
    {
        var probe = healthCheckProbe ?? CreateTestProbe();
        Func<UpstreamTarget, Props> factory = _ => Props.Create<NullActor>();
        if (healthCheckProbe != null)
        {
            factory = _ => Props.Create(() => new ForwardingActor(healthCheckProbe));
        }
        return Sys.ActorOf(Props.Create(() => new UpstreamEntityActor(factory)));
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_reply_UpstreamResolved_on_SelectUpstreamForDomain()
    {
        var target = UpstreamTarget.Create("http://upstream:8080");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var entity = CreateEntity();

        entity.Tell(new RegisterUpstream(target));
        entity.Tell(new SelectUpstreamForDomain(config, "http://upstream:8080/"));

        var resolved = ExpectMsg<UpstreamResolved>();
        Assert.Equal("upstream", resolved.Target.Url.Host);
        Assert.Equal("example.com", resolved.Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_update_target_on_second_RegisterUpstream()
    {
        var first = UpstreamTarget.Create("http://v1:8080");
        var second = UpstreamTarget.Create("http://v2:9090");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var entity = CreateEntity();

        entity.Tell(new RegisterUpstream(first));
        entity.Tell(new RegisterUpstream(second));
        entity.Tell(new SelectUpstreamForDomain(config, "http://v2:9090/"));

        var resolved = ExpectMsg<UpstreamResolved>();
        Assert.Equal("v2", resolved.Target.Url.Host);
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_start_health_check_actor_on_first_RegisterUpstream()
    {
        var healthProbe = CreateTestProbe();
        var target = UpstreamTarget.Create("http://upstream:8080");
        var entity = Sys.ActorOf(Props.Create(() => new UpstreamEntityActor(
            _ => Props.Create(() => new ForwardingActor(healthProbe)))));

        entity.Tell(new RegisterUpstream(target));

        healthProbe.ExpectMsg<RegisterUpstream>(TimeSpan.FromSeconds(1));
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_not_start_second_health_check_on_re_register()
    {
        var startCount = 0;
        var target = UpstreamTarget.Create("http://upstream:8080");

        Func<UpstreamTarget, Props> countingFactory = _ =>
        {
            startCount++;
            return Props.Create<NullActor>();
        };

        var entity = Sys.ActorOf(Props.Create(() => new UpstreamEntityActor(countingFactory)));

        entity.Tell(new RegisterUpstream(target));
        entity.Tell(new RegisterUpstream(target));
        // Give actor time to process both messages
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, startCount);
    }

    // Minimal no-op actor used as stub for HealthCheckActor
    private sealed class NullActor : ReceiveActor { }

    // Forwards the first message it receives to a probe
    private sealed class ForwardingActor : ReceiveActor
    {
        public ForwardingActor(IActorRef probe)
        {
            ReceiveAny(msg => probe.Tell(msg));
        }
    }
}
