using Schleusenwerk.Domain.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Domain.Routing;

public sealed class RouteDefinitionTests
{
    [Fact]
    public void Create_ValidRoute_Succeeds()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var upstreams = new[] { UpstreamTarget.Create("http://localhost:8080") };

        var route = RouteDefinition.Create(config, upstreams);

        Assert.Equal("example.com", route.DomainName.Value);
        Assert.Single(route.Upstreams);
    }

    [Fact]
    public void Create_MultipleUpstreams_Succeeds()
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

    [Fact]
    public void Create_EmptyUpstreams_Throws()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        Assert.Throws<ArgumentException>(() => RouteDefinition.Create(config, []));
    }

    [Fact]
    public void DomainConfig_Defaults_AreCorrect()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        Assert.Equal(RedirectMode.None, config.HttpRedirect);
        Assert.False(config.ForceHttps);
        Assert.True(config.PreserveHostHeader);
        Assert.Equal(TimeSpan.FromSeconds(30), config.RequestTimeout);
    }

    [Fact]
    public void DomainConfig_WithRedirect_SetsMode()
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

    [Fact]
    public void DomainName_ExposedFromConfig()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("api.example.com"),
        };
        var route = RouteDefinition.Create(config, [UpstreamTarget.Create("http://localhost:8080")]);

        Assert.Equal(config.DomainName, route.DomainName);
    }
}
