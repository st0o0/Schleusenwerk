using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class UpstreamTargetSpec
{
    [Fact(Timeout = 5000)]
    public void Create_should_set_default_values()
    {
        var target = UpstreamTarget.Create("http://localhost:8080");

        Assert.Equal("http://localhost:8080/", target.Url.ToString());
        Assert.Equal(1, target.Weight);
        Assert.Equal(100, target.MaxConnections);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_set_custom_values()
    {
        var target = UpstreamTarget.Create("https://api.example.com", weight: 3, maxConnections: 50);

        Assert.Equal(3, target.Weight);
        Assert.Equal(50, target.MaxConnections);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_throw_for_zero_weight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpstreamTarget.Create("http://localhost", weight: 0));
    }

    [Fact(Timeout = 5000)]
    public void Create_should_throw_for_zero_max_connections()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpstreamTarget.Create("http://localhost", maxConnections: 0));
    }

    [Fact(Timeout = 5000)]
    public void Create_should_throw_for_invalid_url()
    {
        Assert.Throws<FormatException>(() => UpstreamTarget.Create("not-a-url"));
    }

    [Fact(Timeout = 5000)]
    public void WithExpression_should_create_new_instance()
    {
        var original = UpstreamTarget.Create("http://localhost:8080", weight: 1);
        var updated = original with { Weight = 5 };

        Assert.Equal(1, original.Weight);
        Assert.Equal(5, updated.Weight);
    }
}
