using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Forwarding;

/// <summary>
/// ASP.NET Core middleware that orchestrates the full proxy pipeline:
/// host resolution via DomainRouterActor, round-robin upstream selection,
/// request forwarding via TurboHTTP, and response header manipulation.
/// </summary>
internal sealed class ProxyRequestHandler
{
    private readonly RequestDelegate _next;
    private readonly IActorRef _domainRouter;
    private readonly RequestForwardingPipeline _forwardingPipeline;
    private readonly HeaderManipulationFilter _headerFilter;
    private readonly WebSocketTunnel _webSocketTunnel;
    private readonly ConcurrentDictionary<DomainName, int> _roundRobinCounters = new();
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
                await HandleResolvedRoute(context, resolved.Route);
                break;

            case UpstreamNotFound:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                break;
        }
    }

    private async Task HandleResolvedRoute(HttpContext context, RouteDefinition route)
    {
        var config = route.Config;

        if (ShouldRedirectToHttps(context, config))
        {
            RedirectToHttps(context, config);
            return;
        }

        var upstream = SelectUpstream(route);
        if (upstream is null)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        if (WebSocketTunnel.IsWebSocketUpgrade(context.Request))
        {
            await _webSocketTunnel.TunnelAsync(context, upstream, config, context.RequestAborted);
            return;
        }

        await _forwardingPipeline.ForwardAsync(context, upstream, config);
        _headerFilter.Apply(context.Response.Headers);
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

    private UpstreamTarget? SelectUpstream(RouteDefinition route)
    {
        var upstreams = route.Upstreams;
        if (upstreams.Count == 0)
        {
            return null;
        }

        if (upstreams.Count == 1)
        {
            return upstreams[0];
        }

        var counter = _roundRobinCounters.AddOrUpdate(
            route.DomainName,
            _ => 0,
            (_, current) => current + 1);

        var index = Math.Abs(counter % upstreams.Count);
        return upstreams[index];
    }
}

internal static class ProxyRequestHandlerExtensions
{
    public static IApplicationBuilder UseProxyRequestHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ProxyRequestHandler>();
    }
}
