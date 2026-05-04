using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class UpstreamUrlSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("http://localhost:8080")]
    [InlineData("https://api.example.com")]
    [InlineData("http://10.0.0.1:3000")]
    public void Parse_should_succeed_for_valid_url(string input)
    {
        var url = UpstreamUrl.Parse(input);
        Assert.Equal(input, url.Value.OriginalString);
    }

    [Theory(Timeout = 5000)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com")]
    [InlineData("not-a-url")]
    [InlineData("tcp://localhost:5000")]
    public void Parse_should_throw_for_invalid_url(string input)
    {
        Assert.Throws<FormatException>(() => UpstreamUrl.Parse(input));
    }

    [Fact(Timeout = 5000)]
    public void Parse_should_throw_for_null()
    {
        Assert.Throws<FormatException>(() => UpstreamUrl.Parse(null!));
    }

    [Fact(Timeout = 5000)]
    public void Properties_should_expose_uri_components()
    {
        var url = UpstreamUrl.Parse("https://api.example.com:8443");

        Assert.Equal("https", url.Scheme);
        Assert.Equal("api.example.com", url.Host);
        Assert.Equal(8443, url.Port);
    }

    [Fact(Timeout = 5000)]
    public void TryParse_should_return_true_for_valid_url()
    {
        Assert.True(UpstreamUrl.TryParse("http://localhost", out var result));
        Assert.Equal("localhost", result.Host);
    }

    [Fact(Timeout = 5000)]
    public void TryParse_should_return_false_for_invalid_url()
    {
        Assert.False(UpstreamUrl.TryParse("not-a-url", out _));
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_treat_same_url_as_equal()
    {
        var a = UpstreamUrl.Parse("http://localhost:8080");
        var b = UpstreamUrl.Parse("http://localhost:8080");

        Assert.Equal(a, b);
    }
}
