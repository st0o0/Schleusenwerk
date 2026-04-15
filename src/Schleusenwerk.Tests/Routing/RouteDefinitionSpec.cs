using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class RouteDefinitionSpec
{
    [Fact(Timeout = 5000)]
    public void Create_should_succeed_for_valid_route()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var upstreams = new[] { UpstreamTarget.Create("http://localhost:8080") };

        var route = RouteDefinition.Create(config, upstreams);

        Assert.Equal("example.com", route.DomainName.Value);
        Assert.Single(route.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_accept_multiple_upstreams()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var upstreams = new[]
        {
            UpstreamTarget.Create("http://localhost:8080", weight: 2),
            UpstreamTarget.Create("http://localhost:8081", weight: 1),
            UpstreamTarget.Create("http://localhost:8082", weight: 3),
        };

        var route = RouteDefinition.Create(config, upstreams);

        Assert.Equal(3, route.Upstreams.Count);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_throw_for_empty_upstreams()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        Assert.Throws<ArgumentException>(() => RouteDefinition.Create(config, []));
    }

    [Fact(Timeout = 5000)]
    public void DomainConfig_should_have_correct_defaults()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        Assert.Equal(RedirectMode.None, config.HttpRedirect);
        Assert.False(config.ForceHttps);
        Assert.True(config.PreserveHostHeader);
        Assert.Equal(TimeSpan.FromSeconds(30), config.RequestTimeout);
    }

    [Fact(Timeout = 5000)]
    public void DomainConfig_should_support_redirect_configuration()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            HttpRedirect = RedirectMode.PermanentRedirect,
            ForceHttps = true,
        };

        Assert.Equal(RedirectMode.PermanentRedirect, config.HttpRedirect);
        Assert.True(config.ForceHttps);
    }

    [Fact(Timeout = 5000)]
    public void DomainName_should_be_exposed_from_config()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("api.example.com"),
        };
        var route = RouteDefinition.Create(config, [UpstreamTarget.Create("http://localhost:8080")]);

        Assert.Equal(config.DomainName, route.DomainName);
    }
}
