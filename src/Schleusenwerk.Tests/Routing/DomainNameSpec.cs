using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainNameSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("example.com", "example.com", false)]
    [InlineData("  Example.COM  ", "example.com", false)]
    [InlineData("sub.example.com", "sub.example.com", false)]
    [InlineData("*.example.com", "*.example.com", true)]
    public void Parse_should_normalize_valid_domain(string input, string expected, bool isWildcard)
    {
        var domain = DomainName.Parse(input);

        Assert.Equal(expected, domain.Value);
        Assert.Equal(isWildcard, domain.IsWildcard);
    }

    [Theory(Timeout = 5000)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*.")]
    [InlineData(".example.com")]
    [InlineData("example..com")]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    public void Parse_should_throw_for_invalid_domain(string input)
    {
        Assert.Throws<FormatException>(() => DomainName.Parse(input));
    }

    [Fact(Timeout = 5000)]
    public void Parse_should_throw_for_null()
    {
        Assert.Throws<FormatException>(() => DomainName.Parse(null!));
    }

    [Fact(Timeout = 5000)]
    public void Matches_should_match_exact_domain()
    {
        var domain = DomainName.Parse("example.com");

        Assert.True(domain.Matches("example.com"));
        Assert.True(domain.Matches("Example.COM"));
        Assert.False(domain.Matches("sub.example.com"));
        Assert.False(domain.Matches("other.com"));
    }

    [Fact(Timeout = 5000)]
    public void Matches_should_match_subdomains_for_wildcard()
    {
        var domain = DomainName.Parse("*.example.com");

        Assert.True(domain.Matches("sub.example.com"));
        Assert.True(domain.Matches("api.example.com"));
        Assert.False(domain.Matches("example.com"));
        Assert.False(domain.Matches("other.com"));
    }

    [Fact(Timeout = 5000)]
    public void TryParse_should_return_true_for_valid_domain()
    {
        Assert.True(DomainName.TryParse("example.com", out var result));
        Assert.Equal("example.com", result.Value);
    }

    [Fact(Timeout = 5000)]
    public void TryParse_should_return_false_for_invalid_domain()
    {
        Assert.False(DomainName.TryParse("", out _));
    }

    [Fact(Timeout = 5000)]
    public void Equals_should_treat_same_domain_as_equal()
    {
        var a = DomainName.Parse("example.com");
        var b = DomainName.Parse("Example.COM");

        Assert.Equal(a, b);
    }

    [Fact(Timeout = 5000)]
    public void ToString_should_return_value()
    {
        var domain = DomainName.Parse("example.com");
        Assert.Equal("example.com", domain.ToString());
    }
}
