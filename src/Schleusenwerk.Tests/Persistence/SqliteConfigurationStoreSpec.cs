using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class SqliteConfigurationStoreSpec : IDisposable
{
    private readonly SqliteConfigurationStore _store;

    public SqliteConfigurationStoreSpec()
    {
        _store = new SqliteConfigurationStore($"Data Source=store-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task GetSettingsAsync_should_return_defaults_when_empty()
    {
        var settings = await _store.GetSettingsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProxySettings.Default.DefaultRequestTimeout, settings.DefaultRequestTimeout);
        Assert.Equal(ProxySettings.Default.MaxConnectionsPerUpstream, settings.MaxConnectionsPerUpstream);
        Assert.False(settings.ForceHttpsGlobally);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateSettingsAsync_should_persist_and_return_settings()
    {
        var settings = new ProxySettings
        {
            ForceHttpsGlobally = true,
            MaxConnectionsPerUpstream = 50,
            Stage = AcmeStage.Production,
        };

        await _store.UpdateSettingsAsync(settings, TestContext.Current.CancellationToken);
        var result = await _store.GetSettingsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.ForceHttpsGlobally);
        Assert.Equal(50, result.MaxConnectionsPerUpstream);
        Assert.Equal(AcmeStage.Production, result.Stage);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateSettingsAsync_should_overwrite_existing()
    {
        await _store.UpdateSettingsAsync(new ProxySettings { ForceHttpsGlobally = true }, TestContext.Current.CancellationToken);
        await _store.UpdateSettingsAsync(new ProxySettings { ForceHttpsGlobally = false, MaxConnectionsPerUpstream = 25 }, TestContext.Current.CancellationToken);

        var result = await _store.GetSettingsAsync(TestContext.Current.CancellationToken);

        Assert.False(result.ForceHttpsGlobally);
        Assert.Equal(25, result.MaxConnectionsPerUpstream);
    }

    [Fact(Timeout = 5000)]
    public async Task GetAllDomainsAsync_should_return_empty_initially()
    {
        var domains = await _store.GetAllDomainsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(domains);
    }

    [Fact(Timeout = 5000)]
    public async Task UpsertDomainAsync_should_add_domain()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);
        var domains = await _store.GetAllDomainsAsync(TestContext.Current.CancellationToken);

        Assert.Single(domains);
        Assert.Equal("example.com", domains[0].DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task UpsertDomainAsync_should_update_existing_domain()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);

        var updated = config with { ForceHttps = true };
        await _store.UpsertDomainAsync(updated, TestContext.Current.CancellationToken);

        var result = await _store.GetDomainAsync(DomainName.Parse("example.com"), TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.ForceHttps);
    }

    [Fact(Timeout = 5000)]
    public async Task GetDomainAsync_should_return_null_for_missing()
    {
        var result = await _store.GetDomainAsync(DomainName.Parse("missing.com"), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomainAsync_should_delete_domain()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);

        await _store.RemoveDomainAsync(DomainName.Parse("example.com"), TestContext.Current.CancellationToken);

        var domains = await _store.GetAllDomainsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(domains);
    }

    [Fact(Timeout = 5000)]
    public async Task UpsertDomainAsync_should_persist_all_fields()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            HttpRedirect = RedirectMode.PermanentRedirect,
            RedirectUrl = new Uri("https://other.com"),
            ForceHttps = true,
            PreserveHostHeader = false,
            WebSocketEnabled = true,
            RequestTimeout = TimeSpan.FromSeconds(60),
        };

        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);
        var result = await _store.GetDomainAsync(DomainName.Parse("example.com"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(RedirectMode.PermanentRedirect, result.HttpRedirect);
        Assert.Equal(new Uri("https://other.com"), result.RedirectUrl);
        Assert.True(result.ForceHttps);
        Assert.False(result.PreserveHostHeader);
        Assert.True(result.WebSocketEnabled);
        Assert.Equal(TimeSpan.FromSeconds(60), result.RequestTimeout);
    }

    [Fact(Timeout = 5000)]
    public async Task UpsertDomainAsync_should_persist_websocket_disabled()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("no-ws.com"),
            WebSocketEnabled = false,
        };

        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);
        var result = await _store.GetDomainAsync(DomainName.Parse("no-ws.com"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task UpsertDomainAsync_should_persist_websocket_enabled()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("ws.com"),
            WebSocketEnabled = true,
        };

        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);
        var result = await _store.GetDomainAsync(DomainName.Parse("ws.com"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task UpsertDomainAsync_should_update_websocket_flag()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("toggle-ws.com"),
            WebSocketEnabled = false,
        };
        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);

        await _store.UpsertDomainAsync(config with { WebSocketEnabled = true }, TestContext.Current.CancellationToken);
        var result = await _store.GetDomainAsync(DomainName.Parse("toggle-ws.com"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public async Task UpsertDomainAsync_should_handle_wildcard_domains()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("*.example.com") };

        await _store.UpsertDomainAsync(config, TestContext.Current.CancellationToken);
        var result = await _store.GetDomainAsync(DomainName.Parse("*.example.com"), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.DomainName.IsWildcard);
    }

    [Fact(Timeout = 5000)]
    public async Task GetAllDomainsAsync_should_return_multiple_domains()
    {
        await _store.UpsertDomainAsync(new DomainConfig { DomainName = DomainName.Parse("a.com") }, TestContext.Current.CancellationToken);
        await _store.UpsertDomainAsync(new DomainConfig { DomainName = DomainName.Parse("b.com") }, TestContext.Current.CancellationToken);
        await _store.UpsertDomainAsync(new DomainConfig { DomainName = DomainName.Parse("c.com") }, TestContext.Current.CancellationToken);

        var domains = await _store.GetAllDomainsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, domains.Count);
    }
}
