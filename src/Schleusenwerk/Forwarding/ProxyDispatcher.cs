using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Forwarding;

internal sealed class ProxyDispatcher : IProxyDispatcher
{
    private readonly IActorRef _domainRouter;
    private readonly RequestForwardingPipeline _pipeline;
    private readonly HeaderManipulationFilter _headerFilter;
    private readonly WebSocketTunnel _webSocketTunnel;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);

    public ProxyDispatcher(
        IRequiredActor<DomainRouterActor> domainRouterProvider,
        RequestForwardingPipeline pipeline,
        HeaderManipulationFilter headerFilter,
        WebSocketTunnel webSocketTunnel)
    {
        _domainRouter = domainRouterProvider.ActorRef;
        _pipeline = pipeline;
        _headerFilter = headerFilter;
        _webSocketTunnel = webSocketTunnel;
    }

    public async Task HandleAsync(HttpContext context, CancellationToken ct)
    {
        var host = context.Request.Host.Host;

        if (string.IsNullOrEmpty(host))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var response = await _domainRouter.Ask<object>(
            new ResolveUpstream(host),
            AskTimeout,
            ct);

        switch (response)
        {
            case UpstreamResolved resolved:
                await HandleResolvedRoute(context, resolved.Target, resolved.Config, ct);
                break;

            case UpstreamNotFound:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                break;
        }
    }

    private async Task HandleResolvedRoute(
        HttpContext context,
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

        await _pipeline.ForwardAsync(context, upstream, config, _headerFilter);
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
