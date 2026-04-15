using Schleusenwerk.Domain.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Domain.Routing;

public sealed class UpstreamUrlTests
{
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://api.example.com")]
    [InlineData("http://10.0.0.1:3000")]
    public void Parse_ValidUrl_Succeeds(string input)
    {
        var url = UpstreamUrl.Parse(input);
        Assert.Equal(input, url.Value.OriginalString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com")]
    [InlineData("not-a-url")]
    [InlineData("tcp://localhost:5000")]
    public void Parse_InvalidUrl_Throws(string input)
    {
        Assert.Throws<FormatException>(() => UpstreamUrl.Parse(input));
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<FormatException>(() => UpstreamUrl.Parse(null!));
    }

    [Fact]
    public void Properties_ExposeUriComponents()
    {
        var url = UpstreamUrl.Parse("https://api.example.com:8443");

        Assert.Equal("https", url.Scheme);
        Assert.Equal("api.example.com", url.Host);
        Assert.Equal(8443, url.Port);
    }

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.True(UpstreamUrl.TryParse("http://localhost", out var result));
        Assert.Equal("localhost", result.Host);
    }

    [Fact]
    public void TryParse_Invalid_ReturnsFalse()
    {
        Assert.False(UpstreamUrl.TryParse("not-a-url", out _));
    }

    [Fact]
    public void StructuralEquality_SameUrl_AreEqual()
    {
        var a = UpstreamUrl.Parse("http://localhost:8080");
        var b = UpstreamUrl.Parse("http://localhost:8080");

        Assert.Equal(a, b);
    }
}
