using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainEntityActorSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private int _actorCounter;

    private (IActorRef entity, IActorRef upstreamProbe) CreateEntity()
    {
        var id = Interlocked.Increment(ref _actorCounter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var upstreamProbe = CreateTestProbe();
        registry.Register<UpstreamEntityActor>(upstreamProbe, overwrite: true);

        var store = new SqliteConfigurationStore($"Data Source=test-{id}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        var entity = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"domain-{id:D4}");
        return (entity, upstreamProbe);
    }

    private static DomainConfig CreateDomainConfig(string domain)
    {
        return new DomainConfig { DomainName = DomainName.Parse(domain) };
    }

    private static UpstreamTarget CreateUpstreamTarget(string url)
    {
        return UpstreamTarget.Create(url);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_persist_config_on_AddDomain()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");

        var result = await entity.Ask<ConfigurationCommandAck>(
            new AddDomain(config), Timeout);

        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reject_duplicate_AddDomain()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        var nack = await entity.Ask<ConfigurationCommandNack>(
            new AddDomain(config), Timeout);

        Assert.Contains("already configured", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_persist_upstream_on_AddUpstream()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream = CreateUpstreamTarget("http://a:8080");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        var result = await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream), Timeout);

        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reject_duplicate_upstream()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream = CreateUpstreamTarget("http://a:8080");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream), Timeout);

        var nack = await entity.Ask<ConfigurationCommandNack>(
            new AddUpstream(config.DomainName, upstream), Timeout);

        Assert.Contains("already exists", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reply_config_on_GetDomainConfig()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream = CreateUpstreamTarget("http://a:8080");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream), Timeout);

        var result = await entity.Ask<DomainConfigResult>(
            GetDomainConfig.Instance, Timeout);

        Assert.Equal(config.DomainName, result.Config.DomainName);
        Assert.Single(result.Upstreams);
        Assert.Equal(upstream.Url, result.Upstreams[0].Url);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_round_robin_across_upstreams()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream1 = CreateUpstreamTarget("http://a:8080");
        var upstream2 = CreateUpstreamTarget("http://b:9090");
        var upstream3 = CreateUpstreamTarget("http://c:7070");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream1), Timeout);

        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream2), Timeout);

        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream3), Timeout);

        var hosts = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var fwd = await entity.Ask<SelectUpstreamForDomain>(
                new ResolveUpstream("example.com"), Timeout);
            hosts.Add(new Uri(fwd.Url).Host);
        }

        Assert.Equal(2, hosts.Count(h => h == "a"));
        Assert.Equal(2, hosts.Count(h => h == "b"));
        Assert.Equal(2, hosts.Count(h => h == "c"));
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_remove_upstream()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream = CreateUpstreamTarget("http://a:8080");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream), Timeout);

        await entity.Ask<ConfigurationCommandAck>(
            new RemoveUpstream(config.DomainName, upstream.Url), Timeout);

        var result = await entity.Ask<DomainConfigResult>(
            GetDomainConfig.Instance, Timeout);
        Assert.Empty(result.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reply_UpstreamNotFound_when_no_upstreams()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);

        var result = await entity.Ask<UpstreamNotFound>(
            new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 10000)]
    public async Task DomainEntityActor_should_handle_health_state_changes()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream1 = CreateUpstreamTarget("http://a:8080");
        var upstream2 = CreateUpstreamTarget("http://b:9090");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream1), Timeout);

        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream2), Timeout);

        // Mark upstream1 as unhealthy
        entity.Tell(new UpstreamHealthChanged(upstream1.Url, IsHealthy: false));

        // Resolve 4 times - should only get upstream2
        for (var i = 0; i < 4; i++)
        {
            var fwd = await entity.Ask<SelectUpstreamForDomain>(
                new ResolveUpstream("example.com"), Timeout);
            Assert.Contains("b", fwd.Url);
        }

        // Mark upstream1 as healthy again
        entity.Tell(new UpstreamHealthChanged(upstream1.Url, IsHealthy: true));

        // Now should balance between both
        var hosts = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var fwd = await entity.Ask<SelectUpstreamForDomain>(
                new ResolveUpstream("example.com"), Timeout);
            hosts.Add(new Uri(fwd.Url).Host);
        }

        Assert.Contains("a", hosts);
        Assert.Contains("b", hosts);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reply_UpstreamNotFound_when_all_unhealthy()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream = CreateUpstreamTarget("http://a:8080");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream), Timeout);

        entity.Tell(new UpstreamHealthChanged(upstream.Url, IsHealthy: false));

        var result = await entity.Ask<UpstreamNotFound>(
            new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reject_AddUpstream_when_domain_not_configured()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream = CreateUpstreamTarget("http://a:8080");

        var nack = await entity.Ask<ConfigurationCommandNack>(
            new AddUpstream(config.DomainName, upstream), Timeout);

        Assert.Contains("not configured", nack.Reason);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reject_RemoveUpstream_when_not_exists()
    {
        var (entity, _) = CreateEntity();
        var config = CreateDomainConfig("example.com");
        var upstream = CreateUpstreamTarget("http://a:8080");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);

        var nack = await entity.Ask<ConfigurationCommandNack>(
            new RemoveUpstream(config.DomainName, upstream.Url), Timeout);

        Assert.Contains("does not exist", nack.Reason);
    }
}
