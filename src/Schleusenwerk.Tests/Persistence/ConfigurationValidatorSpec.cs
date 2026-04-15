using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class ConfigurationValidatorSpec
{
    private static ConfigurationState CreateEmptyState() => new();

    private static ConfigurationState CreateStateWithDomain(string domain)
    {
        var state = new ConfigurationState();
        state.Apply(new DomainAdded(new DomainConfig { DomainName = DomainName.Parse(domain) }));
        return state;
    }

    private static DomainConfig CreateDomainConfig(string domain)
    {
        return new DomainConfig { DomainName = DomainName.Parse(domain) };
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddDomain_should_succeed_for_valid_new_domain()
    {
        var state = CreateEmptyState();
        var config = CreateDomainConfig("example.com");

        var result = ConfigurationValidator.ValidateAddDomain(config, state);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddDomain_should_fail_for_duplicate_domain()
    {
        var state = CreateStateWithDomain("example.com");
        var config = CreateDomainConfig("example.com");

        var result = ConfigurationValidator.ValidateAddDomain(config, state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("already exists", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddDomain_should_succeed_for_wildcard_domain()
    {
        var state = CreateEmptyState();
        var config = new DomainConfig { DomainName = DomainName.Parse("*.example.com") };

        var result = ConfigurationValidator.ValidateAddDomain(config, state);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddDomain_should_fail_for_negative_timeout()
    {
        var state = CreateEmptyState();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            RequestTimeout = TimeSpan.FromSeconds(-1),
        };

        var result = ConfigurationValidator.ValidateAddDomain(config, state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("timeout", ((ConfigurationResult.Failure)result).Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddDomain_should_fail_for_excessive_timeout()
    {
        var state = CreateEmptyState();
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            RequestTimeout = TimeSpan.FromMinutes(30),
        };

        var result = ConfigurationValidator.ValidateAddDomain(config, state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("timeout", ((ConfigurationResult.Failure)result).Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    public void ValidateUpdateDomain_should_succeed_for_existing_domain()
    {
        var state = CreateStateWithDomain("example.com");
        var config = CreateDomainConfig("example.com") with { ForceHttps = true };

        var result = ConfigurationValidator.ValidateUpdateDomain(config, state);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateUpdateDomain_should_fail_for_nonexistent_domain()
    {
        var state = CreateEmptyState();
        var config = CreateDomainConfig("missing.com");

        var result = ConfigurationValidator.ValidateUpdateDomain(config, state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public void ValidateRemoveDomain_should_succeed_for_existing_domain()
    {
        var state = CreateStateWithDomain("example.com");

        var result = ConfigurationValidator.ValidateRemoveDomain(DomainName.Parse("example.com"), state);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateRemoveDomain_should_fail_for_nonexistent_domain()
    {
        var state = CreateEmptyState();

        var result = ConfigurationValidator.ValidateRemoveDomain(DomainName.Parse("missing.com"), state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddUpstream_should_succeed_for_valid_upstream()
    {
        var state = CreateStateWithDomain("example.com");
        var upstream = UpstreamTarget.Create("http://localhost:8080");

        var result = ConfigurationValidator.ValidateAddUpstream(
            DomainName.Parse("example.com"), upstream, state);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddUpstream_should_fail_for_nonexistent_domain()
    {
        var state = CreateEmptyState();
        var upstream = UpstreamTarget.Create("http://localhost:8080");

        var result = ConfigurationValidator.ValidateAddUpstream(
            DomainName.Parse("missing.com"), upstream, state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public void ValidateAddUpstream_should_fail_for_duplicate_upstream()
    {
        var state = CreateStateWithDomain("example.com");
        var upstream = UpstreamTarget.Create("http://localhost:8080");
        state.Apply(new UpstreamAdded(DomainName.Parse("example.com"), upstream));

        var result = ConfigurationValidator.ValidateAddUpstream(
            DomainName.Parse("example.com"), upstream, state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("already exists", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public void ValidateRemoveUpstream_should_succeed_for_existing_upstream()
    {
        var state = CreateStateWithDomain("example.com");
        var upstream = UpstreamTarget.Create("http://localhost:8080");
        state.Apply(new UpstreamAdded(DomainName.Parse("example.com"), upstream));

        var result = ConfigurationValidator.ValidateRemoveUpstream(
            DomainName.Parse("example.com"), UpstreamUrl.Parse("http://localhost:8080"), state);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateRemoveUpstream_should_fail_for_nonexistent_upstream()
    {
        var state = CreateStateWithDomain("example.com");

        var result = ConfigurationValidator.ValidateRemoveUpstream(
            DomainName.Parse("example.com"), UpstreamUrl.Parse("http://localhost:9999"), state);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("does not exist", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public void ValidateDomainFormat_should_succeed_for_valid_domain()
    {
        var result = ConfigurationValidator.ValidateDomainFormat("example.com");
        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateDomainFormat_should_fail_for_empty_string()
    {
        var result = ConfigurationValidator.ValidateDomainFormat("");
        Assert.IsType<ConfigurationResult.Failure>(result);
    }

    [Fact(Timeout = 5000)]
    public void ValidateDomainFormat_should_fail_for_whitespace()
    {
        var result = ConfigurationValidator.ValidateDomainFormat("   ");
        Assert.IsType<ConfigurationResult.Failure>(result);
    }

    [Fact(Timeout = 5000)]
    public void ValidateUpstreamUrlFormat_should_succeed_for_valid_http_url()
    {
        var result = ConfigurationValidator.ValidateUpstreamUrlFormat("http://localhost:8080");
        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateUpstreamUrlFormat_should_succeed_for_valid_https_url()
    {
        var result = ConfigurationValidator.ValidateUpstreamUrlFormat("https://upstream.internal:443");
        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public void ValidateUpstreamUrlFormat_should_fail_for_empty_string()
    {
        var result = ConfigurationValidator.ValidateUpstreamUrlFormat("");
        Assert.IsType<ConfigurationResult.Failure>(result);
    }

    [Fact(Timeout = 5000)]
    public void ValidateUpstreamUrlFormat_should_fail_for_ftp_scheme()
    {
        var result = ConfigurationValidator.ValidateUpstreamUrlFormat("ftp://files.example.com");
        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("http or https", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public void ValidateUpstreamUrlFormat_should_fail_for_relative_url()
    {
        var result = ConfigurationValidator.ValidateUpstreamUrlFormat("/api/health");
        Assert.IsType<ConfigurationResult.Failure>(result);
    }
}
