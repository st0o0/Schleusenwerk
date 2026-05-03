using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Metrics;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Forwarding;

internal sealed class ProxyDispatcher : IProxyDispatcher
{
    private readonly IActorRef _domainRegion;
    private readonly RequestForwardingPipeline _pipeline;
    private readonly HeaderManipulationFilter _headerFilter;
    private readonly WebSocketTunnel _webSocketTunnel;
    private readonly ProxyMetrics _metrics;
    private readonly TimeSpan _resolveTimeout;

    public ProxyDispatcher(
        IRequiredActor<DomainEntityActor> domainRegionProvider,
        RequestForwardingPipeline pipeline,
        HeaderManipulationFilter headerFilter,
        WebSocketTunnel webSocketTunnel,
        ProxyMetrics metrics,
        IConfiguration configuration)
    {
        _domainRegion = domainRegionProvider.ActorRef;
        _pipeline = pipeline;
        _headerFilter = headerFilter;
        _webSocketTunnel = webSocketTunnel;
        _metrics = metrics;

        var seconds = double.TryParse(configuration["Proxy:ResolveTimeoutSeconds"], out var s) ? s : 3;
        _resolveTimeout = TimeSpan.FromSeconds(seconds);
    }

    public async Task HandleAsync(HttpContext context, CancellationToken ct)
    {
        var host = context.Request.Host.Host;

        if (string.IsNullOrEmpty(host))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            _metrics.RecordRequest(host ?? "unknown", context.Response.StatusCode);
            return;
        }

        var response = await _domainRegion.Ask<object>(
            new ResolveUpstream(host),
            _resolveTimeout,
            ct);

        switch (response)
        {
            case UpstreamResolved resolved:
                await HandleResolvedRoute(context, host, resolved.Target, resolved.Config, ct);
                break;

            case UpstreamNotFound:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                break;
        }

        _metrics.RecordRequest(host, context.Response.StatusCode);
    }

    private async Task HandleResolvedRoute(
        HttpContext context,
        string domain,
        UpstreamTarget upstream,
        DomainConfig config,
        CancellationToken ct)
    {
        if (ShouldRedirectToHttps(context, config))
        {
            RedirectToHttps(context, config);
            return;
        }

        if (WebSocketTunnel.IsWebSocketUpgrade(context.Request))
        {
            await _webSocketTunnel.TunnelAsync(context, upstream, config, ct);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _pipeline.ForwardAsync(context, upstream, config, _headerFilter);
        sw.Stop();
        _metrics.RecordDuration(domain, upstream.Url.Value.ToString(), sw.Elapsed.TotalMilliseconds);

        var statusCode = context.Response.StatusCode;
        if (statusCode is >= 502 and <= 504)
        {
            _domainRegion.Tell(new RequestFailed(upstream.Url) { Domain = domain });
        }
        else
        {
            _domainRegion.Tell(new RequestSucceeded(upstream.Url) { Domain = domain });
        }
    }

    private static bool ShouldRedirectToHttps(HttpContext context, DomainConfig config)
    {
        return config.ForceHttps
               && config.HttpRedirect != RedirectMode.None
               && string.Equals(context.Request.Scheme, "http", StringComparison.OrdinalIgnoreCase);
    }

    private static void RedirectToHttps(HttpContext context, DomainConfig config)
    {
        var request = context.Request;
        var httpsUrl = $"https://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
        context.Response.StatusCode = (int)config.HttpRedirect;
        context.Response.Headers.Location = httpsUrl;
    }
}
