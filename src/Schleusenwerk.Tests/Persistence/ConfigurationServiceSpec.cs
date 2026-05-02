using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class ConfigurationServiceSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private int _serviceCounter;

    private ConfigurationService CreateService()
    {
        var id = Interlocked.Increment(ref _serviceCounter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-svc-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var upstreamProbe = CreateTestProbe();
        registry.Register<UpstreamEntityActor>(upstreamProbe, overwrite: true);

        var store = new SqliteConfigurationStore(
            $"Data Source=test-svc-{id}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        var domainActor = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"domain-svc-{id}");
        registry.Register<DomainEntityActor>(domainActor, overwrite: true);

        return new ConfigurationService(registry, store, Timeout);
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
    public async Task AddDomainAsync_should_return_success()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");

        var result = await service.AddDomainAsync(config);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomainAsync_should_return_failure_on_duplicate()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");

        await service.AddDomainAsync(config);
        var result = await service.AddDomainAsync(config);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("already configured", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public async Task GetByDomainAsync_should_return_domain_config()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.GetByDomainAsync(DomainName.Parse("example.com"));

        Assert.IsType<ConfigurationResult<DomainConfigResult>.Success>(result);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.Equal("example.com", domainResult.Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstreamAsync_should_add_upstream_to_domain()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.AddUpstreamAsync(
            DomainName.Parse("example.com"),
            CreateUpstream("http://localhost:8080"));

        Assert.True(result.IsSuccess);

        var queryResult = await service.GetByDomainAsync(DomainName.Parse("example.com"));
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)queryResult).Value;
        Assert.Single(domainResult.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveUpstreamAsync_should_remove_upstream()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);
        await service.AddUpstreamAsync(
            DomainName.Parse("example.com"),
            CreateUpstream("http://localhost:8080"));

        var result = await service.RemoveUpstreamAsync(
            DomainName.Parse("example.com"),
            UpstreamUrl.Parse("http://localhost:8080"));

        Assert.True(result.IsSuccess);

        var queryResult = await service.GetByDomainAsync(DomainName.Parse("example.com"));
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)queryResult).Value;
        Assert.Empty(domainResult.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomainAsync_should_return_success()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.RemoveDomainAsync(DomainName.Parse("example.com"));

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomainAsync_should_update_existing()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.UpdateDomainAsync(config with { ForceHttps = true });

        Assert.True(result.IsSuccess);
        var queryResult = await service.GetByDomainAsync(DomainName.Parse("example.com"));
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)queryResult).Value;
        Assert.True(domainResult.Config.ForceHttps);
    }

    [Fact(Timeout = 5000)]
    public async Task GetSettingsAsync_should_return_default_settings()
    {
        var service = CreateService();

        var result = await service.GetSettingsAsync();

        Assert.IsType<ConfigurationResult<ProxySettings>.Success>(result);
    }
}
