using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Forwarding;

/// <summary>
/// ASP.NET Core middleware that orchestrates the full proxy pipeline:
/// host resolution via DomainRouterActor, request forwarding via TurboHTTP,
/// and response header manipulation.
/// </summary>
internal sealed class ProxyRequestHandler
{
    private readonly RequestDelegate _next;
    private readonly IActorRef _domainRouter;
    private readonly RequestForwardingPipeline _forwardingPipeline;
    private readonly HeaderManipulationFilter _headerFilter;
    private readonly WebSocketTunnel _webSocketTunnel;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);

    public ProxyRequestHandler(
        RequestDelegate next,
        IRequiredActor<DomainRouterActor> domainRouterProvider,
        RequestForwardingPipeline forwardingPipeline,
        HeaderManipulationFilter headerFilter,
        WebSocketTunnel webSocketTunnel)
    {
        _next = next;
        _domainRouter = domainRouterProvider.ActorRef;
        _forwardingPipeline = forwardingPipeline;
        _headerFilter = headerFilter;
        _webSocketTunnel = webSocketTunnel;
    }

    public async Task InvokeAsync(HttpContext context)
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
            context.RequestAborted);

        switch (response)
        {
            case UpstreamResolved resolved:
                await HandleResolvedRoute(context, resolved.Target, resolved.Config);
                break;

            case UpstreamNotFound:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                break;
        }
    }

    private async Task HandleResolvedRoute(HttpContext context, UpstreamTarget upstream, DomainConfig config)
    {
        if (ShouldRedirectToHttps(context, config))
        {
            RedirectToHttps(context, config);
            return;
        }

        if (WebSocketTunnel.IsWebSocketUpgrade(context.Request))
        {
            await _webSocketTunnel.TunnelAsync(context, upstream, config, context.RequestAborted);
            return;
        }

        await _forwardingPipeline.ForwardAsync(context, upstream, config, _headerFilter);
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

internal static class ProxyRequestHandlerExtensions
{
    public static IApplicationBuilder UseProxyRequestHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ProxyRequestHandler>();
    }
}
