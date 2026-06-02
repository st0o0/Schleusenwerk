using System.Diagnostics;

namespace Schleusenwerk.Forwarding;

internal sealed class AccessLogMiddleware
{
    private readonly ILogger<AccessLogMiddleware> _logger;

    public AccessLogMiddleware(ILogger<AccessLogMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        catch (Exception)
        {
            context.Response.StatusCode = 500;
            throw;
        }
        finally
        {
            sw.Stop();
            var upstream = context.Items.TryGetValue("Upstream", out var u) ? u?.ToString() : null;

            _logger.LogInformation(
                "{Method} {Path} {StatusCode} {DurationMs}ms {Domain} {ClientIp} {ContentLength} {UserAgent} {Upstream}",
                context.Request.Method,
                $"{context.Request.Path}{context.Request.QueryString}",
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                context.Request.Host.Host,
                context.Connection.RemoteIpAddress?.ToString() ?? "-",
                context.Response.ContentLength ?? 0,
                context.Request.Headers.UserAgent.ToString(),
                upstream ?? "-");
        }
    }
}
