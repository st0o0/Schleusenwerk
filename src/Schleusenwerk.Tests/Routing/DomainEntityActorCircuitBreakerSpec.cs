using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainEntityActorCircuitBreakerSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private IActorRef CreateEntity()
    {
        var registry = ActorRegistry.For(Sys);
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-cb-{Guid.NewGuid():N}");
        registry.Register<EventHub>(hub, overwrite: true);

        var healthCheckProbe = CreateTestProbe();
        registry.Register<HealthCheckEntityActor>(healthCheckProbe, overwrite: true);

        var store = new SqliteConfigurationStore(
            $"Data Source=cb-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        return Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"entity-cb-{Guid.NewGuid():N}");
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_exclude_upstream_after_consecutive_request_failures()
    {
        var entity = CreateEntity();
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var upstream1 = UpstreamTarget.Create("http://a:8080");
        var upstream2 = UpstreamTarget.Create("http://b:9090");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream1), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream2), Timeout);

        // 3 failures on upstream1 → circuit opens
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });

        await Task.Delay(100);

        // Should only resolve to upstream2
        for (var i = 0; i < 4; i++)
        {
            var resolved = await entity.Ask<UpstreamResolved>(
                new ResolveUpstream("example.com"), Timeout);
            Assert.Equal("b", resolved.Target.Url.Value.Host);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reinclude_upstream_after_health_recovery()
    {
        var entity = CreateEntity();
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var upstream1 = UpstreamTarget.Create("http://a:8080");
        var upstream2 = UpstreamTarget.Create("http://b:9090");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream1), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream2), Timeout);

        // Open circuit on upstream1
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        await Task.Delay(100);

        // Health check says it's back
        entity.Tell(new UpstreamHealthChanged(upstream1.Url, IsHealthy: true));
        await Task.Delay(100);

        // Should resolve to both now
        var hosts = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var resolved = await entity.Ask<UpstreamResolved>(
                new ResolveUpstream("example.com"), Timeout);
            hosts.Add(resolved.Target.Url.Value.Host);
        }

        Assert.Contains("a", hosts);
        Assert.Contains("b", hosts);
    }
}
