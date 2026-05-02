namespace Schleusenwerk.Tests.Persistence;

/// <summary>
/// Tests for state recovery scenarios: empty journal, snapshot + events, snapshot only.
/// Each test gets its own ActorSystem via PersistenceTestKit (xUnit creates a new instance per [Fact]).
/// TODO: Task 5 — rewrite tests for domain registry recovery after removal of AddDomain/GetConfiguration commands.
/// </summary>
#if false
public sealed class ConfigurationRecoverySpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private (IActorRef hub, IActorRef actor) CreateActors(int snapshotInterval = 100, string name = "config")
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        var registry = ActorRegistry.For(Sys);
        registry.Register<EventHub>(hub, overwrite: true);
        var domainProbe = CreateTestProbe();
        registry.Register<DomainEntityActor>(domainProbe, overwrite: true);
        var actor = Sys.ActorOf(Props.Create(() => new ConfigurationPersistenceActor(snapshotInterval)), name);
        return (hub, actor);
    }

    private IActorRef RecoverActor(IActorRef hub, int snapshotInterval = 100, string name = "config-recovered")
    {
        var registry = ActorRegistry.For(Sys);
        registry.Register<EventHub>(hub, overwrite: true);
        registry.Register<DomainEntityActor>(CreateTestProbe(), overwrite: true);
        return Sys.ActorOf(Props.Create(() => new ConfigurationPersistenceActor(snapshotInterval)), name);
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
    public async Task Recovery_should_yield_empty_state_from_empty_journal()
    {
        var (_, actor) = CreateActors();

        var snapshot = await actor.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);

        Assert.Empty(snapshot.Domains);
        Assert.Empty(snapshot.Upstreams);
        Assert.NotNull(snapshot.Settings);
    }

    [Fact(Timeout = 5000)]
    public async Task Recovery_should_rebuild_state_from_journal_events_only()
    {
        var (hub, actor) = CreateActors(snapshotInterval: 0);

        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("a.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("b.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new AddUpstream(DomainName.Parse("a.com"), CreateUpstream("http://localhost:8080")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new UpdateSettings(new ProxySettings { ForceHttpsGlobally = true }), Timeout);

        await actor.GracefulStop(Timeout);

        var recovered = RecoverActor(hub, snapshotInterval: 0);

        var snapshot = await recovered.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);

        Assert.Equal(2, snapshot.Domains.Count);
        Assert.Contains(snapshot.Domains, d => d.DomainName.Value == "a.com");
        Assert.Contains(snapshot.Domains, d => d.DomainName.Value == "b.com");
        Assert.Single(snapshot.Upstreams["a.com"]);
        Assert.True(snapshot.Settings.ForceHttpsGlobally);
    }

    [Fact(Timeout = 5000)]
    public async Task Recovery_should_restore_from_snapshot_only()
    {
        var (hub, actor) = CreateActors(snapshotInterval: 2);

        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("snap.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(
            new AddUpstream(DomainName.Parse("snap.com"), CreateUpstream("http://localhost:3000")), Timeout);

        // 2 events persisted — snapshot should have been taken at event 2
        // Wait briefly for snapshot to be saved
        await Task.Delay(200);

        await actor.GracefulStop(Timeout);

        var recovered = RecoverActor(hub, snapshotInterval: 2);

        var snapshot = await recovered.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);

        Assert.Single(snapshot.Domains);
        Assert.Equal("snap.com", snapshot.Domains[0].DomainName.Value);
        Assert.Single(snapshot.Upstreams["snap.com"]);
    }

    [Fact(Timeout = 5000)]
    public async Task Recovery_should_restore_from_snapshot_plus_subsequent_events()
    {
        var (hub, actor) = CreateActors(snapshotInterval: 2);

        // Events 1-2: triggers snapshot at event 2
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("first.com")), Timeout);
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("second.com")), Timeout);

        // Wait briefly for snapshot to be saved
        await Task.Delay(200);

        // Event 3: after the snapshot — will be replayed from journal on recovery
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("third.com")), Timeout);

        await actor.GracefulStop(Timeout);

        var recovered = RecoverActor(hub, snapshotInterval: 2);

        var snapshot = await recovered.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);

        Assert.Equal(3, snapshot.Domains.Count);
        Assert.Contains(snapshot.Domains, d => d.DomainName.Value == "first.com");
        Assert.Contains(snapshot.Domains, d => d.DomainName.Value == "second.com");
        Assert.Contains(snapshot.Domains, d => d.DomainName.Value == "third.com");
    }

    [Fact(Timeout = 5000)]
    public async Task Recovery_should_restore_settings_from_snapshot_plus_events()
    {
        var (hub, actor) = CreateActors(snapshotInterval: 2);

        // Event 1
        await actor.Ask<ConfigurationCommandAck>(new AddDomain(CreateDomainConfig("x.com")), Timeout);
        // Event 2 — triggers snapshot
        await actor.Ask<ConfigurationCommandAck>(
            new UpdateSettings(new ProxySettings { MaxConnectionsPerUpstream = 42 }), Timeout);

        await Task.Delay(200);

        // Event 3 — after snapshot
        await actor.Ask<ConfigurationCommandAck>(
            new UpdateSettings(new ProxySettings { MaxConnectionsPerUpstream = 42, ForceHttpsGlobally = true }),
            Timeout);

        await actor.GracefulStop(Timeout);

        var recovered = RecoverActor(hub, snapshotInterval: 2);

        var snapshot = await recovered.Ask<ConfigurationSnapshot>(GetConfiguration.Instance, Timeout);

        Assert.Single(snapshot.Domains);
        Assert.True(snapshot.Settings.ForceHttpsGlobally);
        Assert.Equal(42, snapshot.Settings.MaxConnectionsPerUpstream);
    }
}
#endif