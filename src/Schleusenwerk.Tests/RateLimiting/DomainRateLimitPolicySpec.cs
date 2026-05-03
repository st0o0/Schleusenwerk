using Schleusenwerk.RateLimiting;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.RateLimiting;

public sealed class DomainRateLimitPolicySpec
{
    [Fact(Timeout = 5000)]
    public void RateLimitConfigCache_should_return_null_for_unknown_domain()
    {
        var cache = new RateLimitConfigCache();
        var config = cache.GetConfig("unknown.com");
        Assert.Null(config);
    }

    [Fact(Timeout = 5000)]
    public void RateLimitConfigCache_should_return_config_after_update()
    {
        var cache = new RateLimitConfigCache();
        var rateLimit = new RateLimitConfig { RequestsPerWindow = 50, Window = TimeSpan.FromSeconds(30) };
        cache.UpdateConfig("example.com", rateLimit);
        var config = cache.GetConfig("example.com");
        Assert.NotNull(config);
        Assert.Equal(50, config.RequestsPerWindow);
    }

    [Fact(Timeout = 5000)]
    public void RateLimitConfigCache_should_remove_config()
    {
        var cache = new RateLimitConfigCache();
        var rateLimit = new RateLimitConfig();
        cache.UpdateConfig("example.com", rateLimit);
        cache.RemoveConfig("example.com");
        Assert.Null(cache.GetConfig("example.com"));
    }
}
