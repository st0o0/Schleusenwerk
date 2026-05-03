using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Google.Protobuf.WellKnownTypes;
using Schleusenwerk.Contracts;
using Schleusenwerk.Grpc;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Grpc;

public sealed class HealthServiceImplSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private int _counter;

    private (HealthServiceImpl sut, IActorRef domainRegion) CreateSut()
    {
        var id = Interlocked.Increment(ref _counter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-health-svc-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var healthCheckProbe = CreateTestProbe();
        registry.Register<HealthCheckEntityActor>(healthCheckProbe, overwrite: true);

        var store = new SqliteConfigurationStore(
            $"Data Source=health-svc-{id}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        var domainActor = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"domain-health-svc-{id}");
        registry.Register<DomainEntityActor>(domainActor, overwrite: true);

        var sut = new HealthServiceImpl(store, registry, Timeout);
        return (sut, domainActor);
    }

    [Fact(Timeout = 5000)]
    public async Task GetHealth_should_return_zero_counts_when_no_domains()
    {
        var (sut, _) = CreateSut();

        var response = await sut.GetHealth(new Empty(), FakeServerCallContext.Instance);

        Assert.Equal(0, response.RouteCount);
        Assert.Equal(0, response.HealthyCount);
        Assert.Equal(0, response.UnhealthyCount);
    }

    [Fact(Timeout = 5000)]
    public async Task GetUpstreamHealth_should_return_healthy_upstreams_by_default()
    {
        var (sut, domainActor) = CreateSut();
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        domainActor.Tell(new AddDomain(config));
        ExpectMsg<ConfigurationCommandAck>(Timeout);

        var upstream = UpstreamTarget.Create("http://backend:8080");
        domainActor.Tell(new AddUpstream(DomainName.Parse("example.com"), upstream));
        ExpectMsg<ConfigurationCommandAck>(Timeout);

        var request = new GetUpstreamHealthRequest { Domain = "example.com" };
        var response = await sut.GetUpstreamHealth(request, FakeServerCallContext.Instance);

        Assert.Single(response.Upstreams);
        Assert.True(response.Upstreams[0].IsHealthy);
    }
}
