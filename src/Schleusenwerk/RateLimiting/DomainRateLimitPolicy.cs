using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Schleusenwerk.RateLimiting;

public static class DomainRateLimitPolicy
{
    public const string PolicyName = "per-client-per-domain";

    public static RateLimiterOptions ConfigurePolicy(this RateLimiterOptions options, RateLimitConfigCache cache)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(PolicyName, context =>
        {
            var host = context.Request.Host.Host;
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var config = cache.GetConfig(host);
            if (config is null)
            {
                return RateLimitPartition.GetNoLimiter($"{host}:{clientIp}");
            }

            return RateLimitPartition.GetSlidingWindowLimiter(
                $"{host}:{clientIp}",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = config.RequestsPerWindow,
                    Window = config.Window,
                    SegmentsPerWindow = 4,
                    AutoReplenishment = true,
                });
        });
        return options;
    }
}
