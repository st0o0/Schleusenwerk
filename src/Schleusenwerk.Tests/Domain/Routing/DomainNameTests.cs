using Schleusenwerk.Domain.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Domain.Routing;

public sealed class DomainNameTests
{
    [Theory]
    [InlineData("example.com", "example.com", false)]
    [InlineData("  Example.COM  ", "example.com", false)]
    [InlineData("sub.example.com", "sub.example.com", false)]
    [InlineData("*.example.com", "*.example.com", true)]
    public void Parse_ValidDomain_ReturnsNormalized(string input, string expected, bool isWildcard)
    {
        var domain = DomainName.Parse(input);

        Assert.Equal(expected, domain.Value);
        Assert.Equal(isWildcard, domain.IsWildcard);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*.")]
    [InlineData(".example.com")]
    [InlineData("example..com")]
    [InlineData("-example.com")]
    [InlineData("example-.com")]
    public void Parse_InvalidDomain_Throws(string input)
    {
        Assert.Throws<FormatException>(() => DomainName.Parse(input));
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<FormatException>(() => DomainName.Parse(null!));
    }

    [Fact]
    public void Matches_ExactDomain_MatchesExactly()
    {
        var domain = DomainName.Parse("example.com");

        Assert.True(domain.Matches("example.com"));
        Assert.True(domain.Matches("Example.COM"));
        Assert.False(domain.Matches("sub.example.com"));
        Assert.False(domain.Matches("other.com"));
    }

    [Fact]
    public void Matches_WildcardDomain_MatchesSubdomains()
    {
        var domain = DomainName.Parse("*.example.com");

        Assert.True(domain.Matches("sub.example.com"));
        Assert.True(domain.Matches("api.example.com"));
        Assert.False(domain.Matches("example.com"));
        Assert.False(domain.Matches("other.com"));
    }

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.True(DomainName.TryParse("example.com", out var result));
        Assert.Equal("example.com", result.Value);
    }

    [Fact]
    public void TryParse_Invalid_ReturnsFalse()
    {
        Assert.False(DomainName.TryParse("", out _));
    }

    [Fact]
    public void StructuralEquality_SameDomain_AreEqual()
    {
        var a = DomainName.Parse("example.com");
        var b = DomainName.Parse("Example.COM");

        Assert.Equal(a, b);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var domain = DomainName.Parse("example.com");
        Assert.Equal("example.com", domain.ToString());
    }
}
