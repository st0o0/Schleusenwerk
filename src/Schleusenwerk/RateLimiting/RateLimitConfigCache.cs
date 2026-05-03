using System.Collections.Concurrent;
using Schleusenwerk.Routing;

namespace Schleusenwerk.RateLimiting;

public sealed class RateLimitConfigCache
{
    private readonly ConcurrentDictionary<string, RateLimitConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public RateLimitConfig? GetConfig(string domain)
    {
        return _configs.GetValueOrDefault(domain);
    }

    public void UpdateConfig(string domain, RateLimitConfig config)
    {
        _configs[domain] = config;
    }

    public void RemoveConfig(string domain)
    {
        _configs.TryRemove(domain, out _);
    }
}
