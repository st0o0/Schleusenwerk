using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class ConfigurationServiceSpec : PersistenceTestKit
{
    private int _actorCounter;

    private ConfigurationService CreateService()
    {
        var id = Interlocked.Increment(ref _actorCounter);
        var registry = ActorRegistry.For(Sys);
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-svc-{id}");
        registry.Register<EventHub>(hub, overwrite: true);
        var domainProbe = CreateTestProbe();
        registry.Register<DomainEntityActor>(domainProbe, overwrite: true);

        var actor = Sys.ActorOf(
            Props.Create(() => new ConfigurationPersistenceActor()),
            $"config-svc-{id}");
        registry.Register<ConfigurationPersistenceActor>(actor, overwrite: true);

        return new ConfigurationService(registry, TimeSpan.FromSeconds(3));
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
    public async Task GetAllAsync_should_return_empty_snapshot_initially()
    {
        var service = CreateService();

        var result = await service.GetAllAsync();

        Assert.IsType<ConfigurationResult<ConfigurationSnapshot>.Success>(result);
        var snapshot = ((ConfigurationResult<ConfigurationSnapshot>.Success)result).Value;
        Assert.Empty(snapshot.Domains);
        Assert.Empty(snapshot.Upstreams);
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
    public async Task AddDomainAsync_should_add_domain()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");

        await service.AddDomainAsync(config);

        var result = await service.GetAllAsync();
        var snapshot = ((ConfigurationResult<ConfigurationSnapshot>.Success)result).Value;
        Assert.Single(snapshot.Domains);
        Assert.Equal("example.com", snapshot.Domains[0].DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomainAsync_should_return_failure_on_duplicate()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");

        await service.AddDomainAsync(config);

        var result = await service.AddDomainAsync(config);
        Assert.IsType<ConfigurationResult.Failure>(result);
        var failure = (ConfigurationResult.Failure)result;
        Assert.Contains("already exists", failure.Error);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomainAsync_should_update_existing_domain()
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
    public async Task UpdateDomainAsync_should_return_failure_for_nonexistent_domain()
    {
        var service = CreateService();
        var config = CreateDomainConfig("missing.com");

        var result = await service.UpdateDomainAsync(config);
        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomainAsync_should_remove_domain()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");

        await service.AddDomainAsync(config);
        var result = await service.RemoveDomainAsync(DomainName.Parse("example.com"));

        Assert.True(result.IsSuccess);
        var allResult = await service.GetAllAsync();
        var snapshot = ((ConfigurationResult<ConfigurationSnapshot>.Success)allResult).Value;
        Assert.Empty(snapshot.Domains);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomainAsync_should_return_failure_for_nonexistent_domain()
    {
        var service = CreateService();

        var result = await service.RemoveDomainAsync(DomainName.Parse("missing.com"));
        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public async Task GetByDomainAsync_should_return_domain_with_upstreams()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        var domain = DomainName.Parse("example.com");
        var upstream = CreateUpstream("http://localhost:8080");

        await service.AddDomainAsync(config);
        await service.AddUpstreamAsync(domain, upstream);

        var result = await service.GetByDomainAsync(domain);

        Assert.IsType<ConfigurationResult<DomainConfigResult>.Success>(result);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.Equal("example.com", domainResult.Config.DomainName.Value);
        Assert.Single(domainResult.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task GetByDomainAsync_should_return_failure_for_nonexistent_domain()
    {
        var service = CreateService();

        var result = await service.GetByDomainAsync(DomainName.Parse("missing.com"));
        Assert.IsType<ConfigurationResult<DomainConfigResult>.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult<DomainConfigResult>.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstreamAsync_should_add_upstream_to_domain()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        var domain = DomainName.Parse("example.com");

        await service.AddDomainAsync(config);
        await service.AddUpstreamAsync(domain, CreateUpstream("http://localhost:8080"));
        await service.AddUpstreamAsync(domain, CreateUpstream("http://localhost:8081"));

        var result = await service.GetByDomainAsync(domain);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.Equal(2, domainResult.Upstreams.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstreamAsync_should_return_failure_for_nonexistent_domain()
    {
        var service = CreateService();

        var result =
            await service.AddUpstreamAsync(DomainName.Parse("missing.com"), CreateUpstream("http://localhost:8080"));
        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveUpstreamAsync_should_remove_upstream()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        var domain = DomainName.Parse("example.com");

        await service.AddDomainAsync(config);
        await service.AddUpstreamAsync(domain, CreateUpstream("http://localhost:8080"));
        var result = await service.RemoveUpstreamAsync(domain, UpstreamUrl.Parse("http://localhost:8080"));

        Assert.True(result.IsSuccess);
        var queryResult = await service.GetByDomainAsync(domain);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)queryResult).Value;
        Assert.Empty(domainResult.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveUpstreamAsync_should_return_failure_for_nonexistent_upstream()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        var domain = DomainName.Parse("example.com");

        await service.AddDomainAsync(config);

        var result = await service.RemoveUpstreamAsync(domain, UpstreamUrl.Parse("http://localhost:9999"));
        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public async Task GetSettingsAsync_should_return_default_settings()
    {
        var service = CreateService();

        var result = await service.GetSettingsAsync();

        Assert.IsType<ConfigurationResult<ProxySettings>.Success>(result);
        var settings = ((ConfigurationResult<ProxySettings>.Success)result).Value;
        Assert.Equal(ProxySettings.Default.DefaultRequestTimeout, settings.DefaultRequestTimeout);
        Assert.Equal(ProxySettings.Default.MaxConnectionsPerUpstream, settings.MaxConnectionsPerUpstream);
        Assert.False(settings.ForceHttpsGlobally);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateSettingsAsync_should_update_settings()
    {
        var service = CreateService();
        var newSettings = new ProxySettings
        {
            ForceHttpsGlobally = true,
            MaxConnectionsPerUpstream = 50,
        };

        var result = await service.UpdateSettingsAsync(newSettings);

        Assert.True(result.IsSuccess);
        var settingsResult = await service.GetSettingsAsync();
        var settings = ((ConfigurationResult<ProxySettings>.Success)settingsResult).Value;
        Assert.True(settings.ForceHttpsGlobally);
        Assert.Equal(50, settings.MaxConnectionsPerUpstream);
    }

    [Fact(Timeout = 5000)]
    public async Task GetAllAsync_should_reflect_multiple_domains()
    {
        var service = CreateService();

        await service.AddDomainAsync(CreateDomainConfig("a.com"));
        await service.AddDomainAsync(CreateDomainConfig("b.com"));
        await service.AddDomainAsync(CreateDomainConfig("c.com"));

        var result = await service.GetAllAsync();
        var snapshot = ((ConfigurationResult<ConfigurationSnapshot>.Success)result).Value;
        Assert.Equal(3, snapshot.Domains.Count);
    }
}