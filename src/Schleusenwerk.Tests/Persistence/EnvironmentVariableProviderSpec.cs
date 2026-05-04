using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

/// <summary>
/// Fake IConfigurationService for testing ApplyOverridesAsync.
/// Records all calls so tests can verify override behavior.
/// </summary>
file sealed class FakeConfigurationService : IConfigurationService
{
    public List<DomainConfig> AddedDomains { get; } = [];
    public List<DomainConfig> UpdatedDomains { get; } = [];
    public List<(DomainName Domain, UpstreamTarget Upstream)> AddedUpstreams { get; } = [];
    public List<ProxySettings> UpdatedSettings { get; } = [];
    public HashSet<DomainName> ExistingDomains { get; } = [];
    public ProxySettings CurrentSettings { get; set; } = ProxySettings.Default;

    public Task<ConfigurationResult<ConfigurationSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ConfigurationResult<ConfigurationSnapshot>>(
            new ConfigurationResult<ConfigurationSnapshot>.Success(new ConfigurationSnapshot
            {
                Domains = [],
                Upstreams = new Dictionary<string, IReadOnlyList<UpstreamTarget>>(),
                Certificates = new Dictionary<string, CertificateInfo>(),
                Settings = CurrentSettings,
            }));

    public Task<ConfigurationResult<DomainConfigResult>> GetByDomainAsync(DomainName domainName, CancellationToken cancellationToken = default)
        => Task.FromResult<ConfigurationResult<DomainConfigResult>>(
            new ConfigurationResult<DomainConfigResult>.Failure("not found"));

    public Task<ConfigurationResult> AddDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        if (ExistingDomains.Contains(config.DomainName))
        {
            return Task.FromResult<ConfigurationResult>(new ConfigurationResult.Failure("already exists"));
        }

        AddedDomains.Add(config);
        ExistingDomains.Add(config.DomainName);
        return Task.FromResult<ConfigurationResult>(ConfigurationResult.Success.Instance);
    }

    public Task<ConfigurationResult> UpdateDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        UpdatedDomains.Add(config);
        return Task.FromResult<ConfigurationResult>(ConfigurationResult.Success.Instance);
    }

    public Task<ConfigurationResult> RemoveDomainAsync(DomainName domainName, CancellationToken cancellationToken = default)
        => Task.FromResult<ConfigurationResult>(ConfigurationResult.Success.Instance);

    public Task<ConfigurationResult> AddUpstreamAsync(DomainName domainName, UpstreamTarget upstream, CancellationToken cancellationToken = default)
    {
        AddedUpstreams.Add((domainName, upstream));
        return Task.FromResult<ConfigurationResult>(ConfigurationResult.Success.Instance);
    }

    public Task<ConfigurationResult> RemoveUpstreamAsync(DomainName domainName, UpstreamUrl upstreamUrl, CancellationToken cancellationToken = default)
        => Task.FromResult<ConfigurationResult>(ConfigurationResult.Success.Instance);

    public Task<ConfigurationResult<ProxySettings>> GetSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ConfigurationResult<ProxySettings>>(
            new ConfigurationResult<ProxySettings>.Success(CurrentSettings));

    public Task<ConfigurationResult> UpdateSettingsAsync(ProxySettings settings, CancellationToken cancellationToken = default)
    {
        UpdatedSettings.Add(settings);
        CurrentSettings = settings;
        return Task.FromResult<ConfigurationResult>(ConfigurationResult.Success.Instance);
    }

    public Task<ConfigurationResult<string>> ExportAsync(ConfigurationExportOptions? options = null, CancellationToken cancellationToken = default)
        => Task.FromResult<ConfigurationResult<string>>(new ConfigurationResult<string>.Success("{}"));
}

public sealed class EnvironmentVariableProviderSpec
{
    [Fact(Timeout = 5000)]
    public void ParseDomains_should_parse_single_proxy_entry()
    {
        var entries = EnvironmentVariableProvider.ParseDomains("example.com -> http://upstream:80");

        Assert.Single(entries);
        Assert.Equal("example.com", entries[0].DomainName.Value);
        Assert.NotNull(entries[0].Upstream);
        Assert.Equal("upstream", entries[0].Upstream!.Url.Host);
        Assert.Equal(80, entries[0].Upstream!.Url.Port);
        Assert.False(entries[0].IsRedirect);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_parse_comma_separated_entries()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "example.com -> http://web:80, api.example.com -> http://api:3000");

        Assert.Equal(2, entries.Count);
        Assert.Equal("example.com", entries[0].DomainName.Value);
        Assert.Equal("web", entries[0].Upstream!.Url.Host);
        Assert.Equal("api.example.com", entries[1].DomainName.Value);
        Assert.Equal("api", entries[1].Upstream!.Url.Host);
        Assert.Equal(3000, entries[1].Upstream!.Url.Port);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_parse_redirect_entry()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "www.example.com => https://example.com");

        Assert.Single(entries);
        Assert.Equal("www.example.com", entries[0].DomainName.Value);
        Assert.True(entries[0].IsRedirect);
        Assert.Equal("https://example.com/", entries[0].RedirectTarget);
        Assert.Null(entries[0].Upstream);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_parse_mixed_proxy_and_redirect_entries()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "example.com -> http://web:80, www.example.com => https://example.com");

        Assert.Equal(2, entries.Count);
        Assert.False(entries[0].IsRedirect);
        Assert.NotNull(entries[0].Upstream);
        Assert.True(entries[1].IsRedirect);
        Assert.Equal("https://example.com/", entries[1].RedirectTarget);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_handle_https_upstream()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "secure.example.com -> https://backend:443");

        Assert.Single(entries);
        Assert.Equal("https", entries[0].Upstream!.Url.Scheme);
        Assert.Equal(443, entries[0].Upstream!.Url.Port);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_handle_wildcard_domain()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "*.example.com -> http://wildcard:80");

        Assert.Single(entries);
        Assert.True(entries[0].DomainName.IsWildcard);
        Assert.Equal("*.example.com", entries[0].DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_handle_extra_whitespace()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "  example.com   ->   http://web:80  ,  api.example.com  ->  http://api:3000  ");

        Assert.Equal(2, entries.Count);
        Assert.Equal("example.com", entries[0].DomainName.Value);
        Assert.Equal("api.example.com", entries[1].DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_return_empty_list_for_empty_string()
    {
        var entries = EnvironmentVariableProvider.ParseDomains("");

        Assert.Empty(entries);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_return_empty_list_for_whitespace()
    {
        var entries = EnvironmentVariableProvider.ParseDomains("   ");

        Assert.Empty(entries);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_missing_operator()
    {
        var ex = Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("example.com http://upstream:80"));

        Assert.Contains("Invalid DOMAINS entry", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_missing_domain()
    {
        var ex = Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("-> http://upstream:80"));

        Assert.Contains("Missing domain name", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_missing_upstream()
    {
        var ex = Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("example.com ->"));

        Assert.Contains("Missing upstream URL", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_missing_redirect_target()
    {
        var ex = Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("www.example.com =>"));

        Assert.Contains("Missing redirect target", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_invalid_upstream_url()
    {
        Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("example.com -> not-a-url"));
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_invalid_redirect_url()
    {
        var ex = Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("www.example.com => not-a-url"));

        Assert.Contains("Invalid redirect target", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_ftp_redirect_target()
    {
        var ex = Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("www.example.com => ftp://files.example.com"));

        Assert.Contains("http or https", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_throw_for_invalid_domain_name()
    {
        Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseDomains("-invalid.com -> http://upstream:80"));
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_parse_three_entries()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "a.com -> http://a:80, b.com -> http://b:80, c.com -> http://c:80");

        Assert.Equal(3, entries.Count);
        Assert.Equal("a.com", entries[0].DomainName.Value);
        Assert.Equal("b.com", entries[1].DomainName.Value);
        Assert.Equal("c.com", entries[2].DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_default_to_local_for_null()
    {
        var stage = EnvironmentVariableProvider.ParseStage(null);

        Assert.Equal(AcmeStage.Local, stage);
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_default_to_local_for_empty()
    {
        var stage = EnvironmentVariableProvider.ParseStage("");

        Assert.Equal(AcmeStage.Local, stage);
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_parse_local()
    {
        var stage = EnvironmentVariableProvider.ParseStage("local");

        Assert.Equal(AcmeStage.Local, stage);
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_parse_staging()
    {
        var stage = EnvironmentVariableProvider.ParseStage("staging");

        Assert.Equal(AcmeStage.Staging, stage);
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_parse_production()
    {
        var stage = EnvironmentVariableProvider.ParseStage("production");

        Assert.Equal(AcmeStage.Production, stage);
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_be_case_insensitive()
    {
        Assert.Equal(AcmeStage.Production, EnvironmentVariableProvider.ParseStage("PRODUCTION"));
        Assert.Equal(AcmeStage.Staging, EnvironmentVariableProvider.ParseStage("Staging"));
        Assert.Equal(AcmeStage.Local, EnvironmentVariableProvider.ParseStage("LOCAL"));
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_trim_whitespace()
    {
        var stage = EnvironmentVariableProvider.ParseStage("  production  ");

        Assert.Equal(AcmeStage.Production, stage);
    }

    [Fact(Timeout = 5000)]
    public void ParseStage_should_throw_for_invalid_value()
    {
        var ex = Assert.Throws<FormatException>(
            () => EnvironmentVariableProvider.ParseStage("invalid"));

        Assert.Contains("Invalid STAGE value", ex.Message);
        Assert.Contains("invalid", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void DomainEntry_should_report_redirect_correctly()
    {
        var proxyEntry = new EnvironmentVariableProvider.DomainEntry
        {
            DomainName = DomainName.Parse("example.com"),
            Upstream = UpstreamTarget.Create("http://web:80"),
        };

        var redirectEntry = new EnvironmentVariableProvider.DomainEntry
        {
            DomainName = DomainName.Parse("www.example.com"),
            RedirectTarget = "https://example.com/",
        };

        Assert.False(proxyEntry.IsRedirect);
        Assert.True(redirectEntry.IsRedirect);
    }

    [Fact(Timeout = 5000)]
    public void ParseDomains_should_handle_redirect_with_path()
    {
        var entries = EnvironmentVariableProvider.ParseDomains(
            "old.example.com => https://new.example.com/landing");

        Assert.Single(entries);
        Assert.True(entries[0].IsRedirect);
        Assert.Contains("landing", entries[0].RedirectTarget!);
    }

    [Fact(Timeout = 5000)]
    public async Task ApplyOverridesAsync_should_add_new_proxy_domain_and_upstream()
    {
        var service = new FakeConfigurationService();
        var config = new EnvironmentVariableProvider.EnvironmentConfig
        {
            Entries = EnvironmentVariableProvider.ParseDomains("example.com -> http://web:80"),
            Stage = AcmeStage.Local,
        };

        await EnvironmentVariableProvider.ApplyOverridesAsync(config, service);

        Assert.Single(service.AddedDomains);
        Assert.Equal("example.com", service.AddedDomains[0].DomainName.Value);
        Assert.Single(service.AddedUpstreams);
        Assert.Equal("example.com", service.AddedUpstreams[0].Domain.Value);
        Assert.Equal("web", service.AddedUpstreams[0].Upstream.Url.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task ApplyOverridesAsync_should_update_existing_domain()
    {
        var service = new FakeConfigurationService();
        service.ExistingDomains.Add(DomainName.Parse("example.com"));
        var config = new EnvironmentVariableProvider.EnvironmentConfig
        {
            Entries = EnvironmentVariableProvider.ParseDomains("example.com -> http://web:80"),
            Stage = AcmeStage.Local,
        };

        await EnvironmentVariableProvider.ApplyOverridesAsync(config, service);

        Assert.Empty(service.AddedDomains);
        Assert.Single(service.UpdatedDomains);
        Assert.Equal("example.com", service.UpdatedDomains[0].DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task ApplyOverridesAsync_should_add_redirect_domain_without_upstream()
    {
        var service = new FakeConfigurationService();
        var config = new EnvironmentVariableProvider.EnvironmentConfig
        {
            Entries = EnvironmentVariableProvider.ParseDomains("www.example.com => https://example.com"),
            Stage = AcmeStage.Local,
        };

        await EnvironmentVariableProvider.ApplyOverridesAsync(config, service);

        Assert.Single(service.AddedDomains);
        Assert.Equal("www.example.com", service.AddedDomains[0].DomainName.Value);
        Assert.Equal(RedirectMode.PermanentRedirect, service.AddedDomains[0].HttpRedirect);
        Assert.NotNull(service.AddedDomains[0].RedirectUrl);
        Assert.Equal("https://example.com/", service.AddedDomains[0].RedirectUrl!.ToString());
        Assert.Empty(service.AddedUpstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task ApplyOverridesAsync_should_apply_stage_to_settings()
    {
        var service = new FakeConfigurationService();
        var config = new EnvironmentVariableProvider.EnvironmentConfig
        {
            Entries = [],
            Stage = AcmeStage.Production,
        };

        await EnvironmentVariableProvider.ApplyOverridesAsync(config, service);

        Assert.Single(service.UpdatedSettings);
        Assert.Equal(AcmeStage.Production, service.UpdatedSettings[0].Stage);
    }

    [Fact(Timeout = 5000)]
    public async Task ApplyOverridesAsync_should_override_existing_settings_stage()
    {
        var service = new FakeConfigurationService
        {
            CurrentSettings = new ProxySettings { Stage = AcmeStage.Local },
        };
        var config = new EnvironmentVariableProvider.EnvironmentConfig
        {
            Entries = [],
            Stage = AcmeStage.Staging,
        };

        await EnvironmentVariableProvider.ApplyOverridesAsync(config, service);

        Assert.Equal(AcmeStage.Staging, service.CurrentSettings.Stage);
    }

    [Fact(Timeout = 5000)]
    public async Task ApplyOverridesAsync_should_handle_multiple_entries()
    {
        var service = new FakeConfigurationService();
        var config = new EnvironmentVariableProvider.EnvironmentConfig
        {
            Entries = EnvironmentVariableProvider.ParseDomains(
                "a.com -> http://a:80, b.com -> http://b:80, www.a.com => https://a.com"),
            Stage = AcmeStage.Staging,
        };

        await EnvironmentVariableProvider.ApplyOverridesAsync(config, service);

        Assert.Equal(3, service.AddedDomains.Count);
        Assert.Equal(2, service.AddedUpstreams.Count);
        Assert.Equal(AcmeStage.Staging, service.CurrentSettings.Stage);
    }
}
