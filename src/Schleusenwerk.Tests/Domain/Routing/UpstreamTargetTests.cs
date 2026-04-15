using Schleusenwerk.Domain.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Domain.Routing;

public sealed class UpstreamTargetTests
{
    [Fact]
    public void Create_WithDefaults_SetsDefaultValues()
    {
        var target = UpstreamTarget.Create("http://localhost:8080");

        Assert.Equal("http://localhost:8080/", target.Url.ToString());
        Assert.Equal(1, target.Weight);
        Assert.Equal(100, target.MaxConnections);
    }

    [Fact]
    public void Create_WithCustomValues_SetsValues()
    {
        var target = UpstreamTarget.Create("https://api.example.com", weight: 3, maxConnections: 50);

        Assert.Equal(3, target.Weight);
        Assert.Equal(50, target.MaxConnections);
    }

    [Fact]
    public void Create_ZeroWeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpstreamTarget.Create("http://localhost", weight: 0));
    }

    [Fact]
    public void Create_ZeroMaxConnections_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UpstreamTarget.Create("http://localhost", maxConnections: 0));
    }

    [Fact]
    public void Create_InvalidUrl_Throws()
    {
        Assert.Throws<FormatException>(() => UpstreamTarget.Create("not-a-url"));
    }

    [Fact]
    public void Immutability_WithExpression_CreatesNew()
    {
        var original = UpstreamTarget.Create("http://localhost:8080", weight: 1);
        var updated = original with { Weight = 5 };

        Assert.Equal(1, original.Weight);
        Assert.Equal(5, updated.Weight);
    }
}
