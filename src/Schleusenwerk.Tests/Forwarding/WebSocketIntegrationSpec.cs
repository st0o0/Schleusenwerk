using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class WebSocketIntegrationSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private int _counter;

    private ConfigurationService CreateService()
    {
        var id = Interlocked.Increment(ref _counter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-ws-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var healthCheckProbe = CreateTestProbe();
        registry.Register<HealthCheckEntityActor>(healthCheckProbe, overwrite: true);

        var store = new SqliteConfigurationStore(
            $"Data Source=test-ws-{id}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        var domainActor = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"domain-ws-{id}");
        registry.Register<DomainEntityActor>(domainActor, overwrite: true);

        return new ConfigurationService(registry, store, Timeout);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomain_should_persist_websocket_disabled()
    {
        var service = CreateService();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("ws-off.example.com"),
            WebSocketEnabled = false,
        };

        await service.AddDomainAsync(config, TestContext.Current.CancellationToken);

        var result = await service.GetByDomainAsync(DomainName.Parse("ws-off.example.com"), TestContext.Current.CancellationToken);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.False(domainResult.Config.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomain_should_persist_websocket_enabled()
    {
        var service = CreateService();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("ws-on.example.com"),
            WebSocketEnabled = true,
        };

        await service.AddDomainAsync(config, TestContext.Current.CancellationToken);

        var result = await service.GetByDomainAsync(DomainName.Parse("ws-on.example.com"), TestContext.Current.CancellationToken);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.True(domainResult.Config.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomain_should_toggle_websocket_on()
    {
        var service = CreateService();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("toggle.example.com"),
            WebSocketEnabled = false,
        };
        await service.AddDomainAsync(config, TestContext.Current.CancellationToken);

        var updated = config with { WebSocketEnabled = true };
        await service.UpdateDomainAsync(updated, TestContext.Current.CancellationToken);

        var result = await service.GetByDomainAsync(DomainName.Parse("toggle.example.com"), TestContext.Current.CancellationToken);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.True(domainResult.Config.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomain_should_toggle_websocket_off()
    {
        var service = CreateService();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("toggle-off.example.com"),
            WebSocketEnabled = true,
        };
        await service.AddDomainAsync(config, TestContext.Current.CancellationToken);

        var updated = config with { WebSocketEnabled = false };
        await service.UpdateDomainAsync(updated, TestContext.Current.CancellationToken);

        var result = await service.GetByDomainAsync(DomainName.Parse("toggle-off.example.com"), TestContext.Current.CancellationToken);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.False(domainResult.Config.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_return_websocket_config()
    {
        var service = CreateService();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("resolve-ws.example.com"),
            WebSocketEnabled = true,
        };
        await service.AddDomainAsync(config, TestContext.Current.CancellationToken);
        await service.AddUpstreamAsync(
            DomainName.Parse("resolve-ws.example.com"),
            UpstreamTarget.Create("http://backend:8080"),
            TestContext.Current.CancellationToken);

        var registry = ActorRegistry.For(Sys);
        var domainActor = registry.Get<DomainEntityActor>();
        var response = await domainActor.Ask<object>(
            new ResolveUpstream("resolve-ws.example.com"), Timeout, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<UpstreamResolved>(response);
        Assert.True(resolved.Config.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveUpstream_should_return_websocket_disabled_config()
    {
        var service = CreateService();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("no-ws.example.com"),
        };
        await service.AddDomainAsync(config, TestContext.Current.CancellationToken);
        await service.AddUpstreamAsync(
            DomainName.Parse("no-ws.example.com"),
            UpstreamTarget.Create("http://backend:8080"),
            TestContext.Current.CancellationToken);

        var registry = ActorRegistry.For(Sys);
        var domainActor = registry.Get<DomainEntityActor>();
        var response = await domainActor.Ask<object>(
            new ResolveUpstream("no-ws.example.com"), Timeout, TestContext.Current.CancellationToken);

        var resolved = Assert.IsType<UpstreamResolved>(response);
        Assert.False(resolved.Config.WebSocketEnabled);
    }
}
