using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Akka.Streams;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class ConfigurationPersistenceActorSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private int _actorCounter;

    private (IActorRef hub, IActorRef config) CreateActors(int snapshotInterval = 100)
    {
        var id = Interlocked.Increment(ref _actorCounter);
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{id}");
        var registry = ActorRegistry.For(Sys);
        registry.Register<EventHub>(hub);
        var domainProbe = CreateTestProbe();
        registry.Register<DomainEntityActor>(domainProbe);
        var config = Sys.ActorOf(Props.Create(() => new ConfigurationPersistenceActor(snapshotInterval)), $"config-{id:D4}");
        return (hub, config);
    }

    private static DomainConfig CreateDomainConfig(string domain)
    {
        return new DomainConfig { DomainName = DomainName.Parse(domain) };
    }

    private static UpstreamTarget CreateUpstream(string url)
    {
        return UpstreamTarget.Create(url);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomain_should_acknowledge_new_domain()
    {
        var (_, actor) = CreateActors();
        var result = await actor.Ask<ConfigurationCommandAck>(
            new AddDomain(CreateDomainConfig("example.com")), Timeout);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomain_should_reject_duplicate_domain()
    {
        var (_, actor) = CreateActors();
        var config = CreateDomainConfig("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        var nack = await actor.Ask<ConfigurationCommandNack>(new AddDomain(config), Timeout);
        Assert.Contains("already exists", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomain_should_acknowledge_existing_domain()
    {
        var (_, actor) = CreateActors();
        var config = CreateDomainConfig("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        var result = await actor.Ask<ConfigurationCommandAck>(
            new UpdateDomain(config with { ForceHttps = true }), Timeout);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomain_should_reject_nonexistent_domain()
    {
        var (_, actor) = CreateActors();
        var nack = await actor.Ask<ConfigurationCommandNack>(
            new UpdateDomain(CreateDomainConfig("missing.com")), Timeout);
        Assert.Contains("does not exist", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomain_should_acknowledge_existing_domain()
    {
        var (_, actor) = CreateActors();
        await actor.Ask<ConfigurationCommandAck>(
            new AddDomain(CreateDomainConfig("example.com")), Timeout);
        var result = await actor.Ask<ConfigurationCommandAck>(
            new Schleusenwerk.Persistence.RemoveDomain(DomainName.Parse("example.com")), Timeout);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomain_should_reject_nonexistent_domain()
    {
        var (_, actor) = CreateActors();
        var nack = await actor.Ask<ConfigurationCommandNack>(
            new Schleusenwerk.Persistence.RemoveDomain(DomainName.Parse("missing.com")), Timeout);
        Assert.Contains("does not exist", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstream_should_acknowledge_for_existing_domain()
    {
        var (_, actor) = CreateActors();
        var domain = DomainName.Parse("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("example.com")), Timeout);
        var result = await actor.Ask<ConfigurationCommandAck>(
            new AddUpstream(domain, CreateUpstream("http://localhost:8080")), Timeout);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstream_should_reject_for_nonexistent_domain()
    {
        var (_, actor) = CreateActors();
        var nack = await actor.Ask<ConfigurationCommandNack>(
            new AddUpstream(DomainName.Parse("missing.com"), CreateUpstream("http://localhost:8080")), Timeout);
        Assert.Contains("does not exist", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstream_should_reject_duplicate_upstream()
    {
        var (_, actor) = CreateActors();
        var domain = DomainName.Parse("example.com");
        var upstream = CreateUpstream("http://localhost:8080");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("example.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(new AddUpstream(domain, upstream), Timeout);
        var nack = await actor.Ask<ConfigurationCommandNack>(new AddUpstream(domain, upstream), Timeout);
        Assert.Contains("already exists", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveUpstream_should_acknowledge_existing_upstream()
    {
        var (_, actor) = CreateActors();
        var domain = DomainName.Parse("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("example.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new AddUpstream(domain, CreateUpstream("http://localhost:8080")), Timeout);
        var result = await actor.Ask<ConfigurationCommandAck>(
            new RemoveUpstream(domain, UpstreamUrl.Parse("http://localhost:8080")), Timeout);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveUpstream_should_reject_nonexistent_upstream()
    {
        var (_, actor) = CreateActors();
        var domain = DomainName.Parse("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("example.com")), Timeout);
        var nack = await actor.Ask<ConfigurationCommandNack>(
            new RemoveUpstream(domain, UpstreamUrl.Parse("http://localhost:9999")), Timeout);
        Assert.Contains("does not exist", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateSettings_should_acknowledge()
    {
        var (_, actor) = CreateActors();
        var result = await actor.Ask<ConfigurationCommandAck>(
            new UpdateSettings(new ProxySettings { ForceHttpsGlobally = true }), Timeout);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task GetConfiguration_should_return_current_state()
    {
        var (_, actor) = CreateActors();
        var domain = DomainName.Parse("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("example.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new AddUpstream(domain, CreateUpstream("http://localhost:8080")), Timeout);
        var snapshot = await actor.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);
        Assert.Single(snapshot.Domains);
        Assert.Equal("example.com", snapshot.Domains[0].DomainName.Value);
        Assert.Single(snapshot.Upstreams["example.com"]);
    }

    [Fact(Timeout = 5000)]
    public async Task GetConfiguration_should_reflect_settings_update()
    {
        var (_, actor) = CreateActors();
        var settings = new ProxySettings
        {
            ForceHttpsGlobally = true,
            MaxConnectionsPerUpstream = 50,
        };
        await actor.Ask<ConfigurationCommandAck>(new UpdateSettings(settings), Timeout);
        var snapshot = await actor.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);
        Assert.True(snapshot.Settings.ForceHttpsGlobally);
        Assert.Equal(50, snapshot.Settings.MaxConnectionsPerUpstream);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomain_should_remove_domain_and_upstreams_from_state()
    {
        var (_, actor) = CreateActors();
        var domain = DomainName.Parse("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("example.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new AddUpstream(domain, CreateUpstream("http://localhost:8080")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new Schleusenwerk.Persistence.RemoveDomain(domain), Timeout);
        var snapshot = await actor.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);
        Assert.Empty(snapshot.Domains);
        Assert.Empty(snapshot.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomain_should_preserve_upstreams()
    {
        var (_, actor) = CreateActors();
        var domain = DomainName.Parse("example.com");
        var config = CreateDomainConfig("example.com");
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new AddUpstream(domain, CreateUpstream("http://localhost:8080")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new UpdateDomain(config with { ForceHttps = true }), Timeout);
        var snapshot = await actor.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);
        Assert.True(snapshot.Domains[0].ForceHttps);
        Assert.Single(snapshot.Upstreams["example.com"]);
    }

    [Fact(Timeout = 10000)]
    public async Task ConfigurationPersistenceActor_should_publish_DomainAdded_via_hub()
    {
        var (hub, actor) = CreateActors();
        var subscribed = await hub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance, Timeout);
        var tcs = new TaskCompletionSource<IClusterEvent>();
        subscribed.SourceRef.Source.RunForeach(e => tcs.TrySetResult(e), Sys.Materializer());
        // Allow BroadcastHub subscriber stream to fully materialize
        await Task.Delay(500);

        await actor.Ask<ConfigurationCommandAck>(
            new AddDomain(CreateDomainConfig("publish-test.com")), Timeout);

        var evt = await tcs.Task.WaitAsync(Timeout);
        Assert.IsType<DomainAdded>(evt);
        Assert.Equal("publish-test.com", ((DomainAdded)evt).Config.DomainName.Value);
    }

    [Fact(Timeout = 10000)]
    public async Task ConfigurationPersistenceActor_should_publish_CertificateProvisioningRequested_on_domain_add()
    {
        var (hub, actor) = CreateActors();
        var subscribed = await hub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance, Timeout);
        var events = new List<IClusterEvent>();
        var secondTcs = new TaskCompletionSource<bool>();
        subscribed.SourceRef.Source.RunForeach(e =>
        {
            events.Add(e);
            if (events.Count >= 2) secondTcs.TrySetResult(true);
        }, Sys.Materializer());
        // Allow BroadcastHub subscriber stream to fully materialize
        await Task.Delay(500);

        await actor.Ask<ConfigurationCommandAck>(
            new AddDomain(CreateDomainConfig("cert-test.com")), Timeout);

        await secondTcs.Task.WaitAsync(Timeout);
        Assert.Contains(events, e => e is DomainAdded da && da.Config.DomainName.Value == "cert-test.com");
        Assert.Contains(events,
            e => e is CertificateProvisioningRequested cpr && cpr.DomainName.Value == "cert-test.com");
    }
}