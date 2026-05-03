using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainEntityActorHealthSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private IActorRef CreateEntity(string domain)
    {
        var registry = ActorRegistry.For(Sys);
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-health-{Guid.NewGuid():N}");
        registry.Register<EventHub>(hub, overwrite: true);

        var upstreamProbe = CreateTestProbe();
        registry.Register<UpstreamEntityActor>(upstreamProbe, overwrite: true);

        var store = new SqliteConfigurationStore(
            $"Data Source=health-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        return Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"entity-health-{Guid.NewGuid():N}");
    }

    [Fact(Timeout = 5000)]
    public async Task GetDomainUpstreamHealth_should_return_empty_when_no_upstreams()
    {
        var entity = CreateEntity("example.com");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        entity.Tell(new AddDomain(config));
        ExpectMsg<ConfigurationCommandAck>(Timeout);

        var result = await entity.Ask<DomainUpstreamHealthResult>(
            new GetDomainUpstreamHealth { Domain = "example.com" }, Timeout);

        Assert.Empty(result.Entries);
    }

    [Fact(Timeout = 5000)]
    public async Task GetDomainUpstreamHealth_should_mark_unhealthy_upstreams()
    {
        var entity = CreateEntity("example.com");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        entity.Tell(new AddDomain(config));
        ExpectMsg<ConfigurationCommandAck>(Timeout);

        var upstream = UpstreamTarget.Create("http://backend:8080");
        entity.Tell(new AddUpstream(DomainName.Parse("example.com"), upstream));
        ExpectMsg<ConfigurationCommandAck>(Timeout);

        entity.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://backend:8080"), IsHealthy: false));
        await Task.Delay(100);

        var result = await entity.Ask<DomainUpstreamHealthResult>(
            new GetDomainUpstreamHealth { Domain = "example.com" }, Timeout);

        Assert.Single(result.Entries);
        Assert.False(result.Entries[0].IsHealthy);
    }

    [Fact(Timeout = 5000)]
    public async Task GetDomainUpstreamHealth_should_return_healthy_by_default()
    {
        var entity = CreateEntity("example.com");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        entity.Tell(new AddDomain(config));
        ExpectMsg<ConfigurationCommandAck>(Timeout);

        var upstream = UpstreamTarget.Create("http://backend:8080");
        entity.Tell(new AddUpstream(DomainName.Parse("example.com"), upstream));
        ExpectMsg<ConfigurationCommandAck>(Timeout);

        var result = await entity.Ask<DomainUpstreamHealthResult>(
            new GetDomainUpstreamHealth { Domain = "example.com" }, Timeout);

        Assert.Single(result.Entries);
        Assert.True(result.Entries[0].IsHealthy);
        Assert.Equal("http://backend:8080/", result.Entries[0].Url.Value.ToString());
    }
}
