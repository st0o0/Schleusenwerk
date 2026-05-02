using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schleusenwerk.Forwarding;
using Schleusenwerk.LoadBalancing;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using TurboHTTP;
using Xunit;

namespace Schleusenwerk.Tests.Integration;

public sealed class ProxyPipelineSpec : IAsyncLifetime
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private ActorSystem _actorSystem = null!;
    private IHost _upstreamHost = null!;
    private IHost _proxyHost = null!;
    private HttpClient _proxyClient = null!;
    private int _upstreamPort;
    private readonly Channel<CapturedRequest> _requestCapture =
        Channel.CreateBounded<CapturedRequest>(new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });

    public async ValueTask InitializeAsync()
    {
        _actorSystem = ActorSystem.Create("schleusenwerk-test");
        var registry = ActorRegistry.For(_actorSystem);

        var hub = _actorSystem.ActorOf(Props.Create<EventHub>(), "eventHub");
        registry.Register<EventHub>(hub);
        var router = _actorSystem.ActorOf(
            Props.Create(() => new DomainRouterActor(
                upstreams => Props.Create(() => new LoadBalancerActor(upstreams)))),
            "domain-router");
        registry.Register<DomainRouterActor>(router);

        _upstreamHost = BuildUpstreamHost();
        await _upstreamHost.StartAsync();
        _upstreamPort = GetServerPort(_upstreamHost);
        _proxyHost = BuildProxyHost(registry);
        await _proxyHost.StartAsync();
        var proxyPort = GetServerPort(_proxyHost);
        _proxyClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{proxyPort}") };
    }

    public async ValueTask DisposeAsync()
    {
        _proxyClient.Dispose();
        await _proxyHost.StopAsync();
        await _upstreamHost.StopAsync();
        _proxyHost.Dispose();
        _upstreamHost.Dispose();
        await _actorSystem.Terminate();
        _actorSystem.Dispose();
    }

    [Fact(Timeout = 10000)]
    public async Task Proxy_should_return_upstream_status_code_and_body()
    {
        var route = CreateRoute("test.local", $"http://127.0.0.1:{_upstreamPort}");
        var router = GetRouter();
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("test.local"), AskTimeout);

        var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
        request.Headers.Host = "test.local";
        var response = await _proxyClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Hello from upstream", body);
    }

    [Fact(Timeout = 10000)]
    public async Task Proxy_should_forward_proxy_headers_to_upstream()
    {
        var route = CreateRoute("headers.local", $"http://127.0.0.1:{_upstreamPort}");
        var router = GetRouter();
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("headers.local"), AskTimeout);

        var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Host = "headers.local";
        using var cts = new CancellationTokenSource(AskTimeout);
        await _proxyClient.SendAsync(request, cts.Token);
        var captured = await _requestCapture.Reader.ReadAsync(cts.Token);

        Assert.True(captured.Headers.ContainsKey("X-Forwarded-For"), "Upstream should receive X-Forwarded-For");
        Assert.True(captured.Headers.ContainsKey("X-Forwarded-Host"), "Upstream should receive X-Forwarded-Host");
        Assert.Contains("headers.local", captured.Headers["X-Forwarded-Host"]);
    }

    private IActorRef GetRouter()
        => _actorSystem.ActorSelection("/user/domain-router").ResolveOne(AskTimeout).GetAwaiter().GetResult();

    private IHost BuildUpstreamHost()
    {
        return new HostBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
                web.Configure(app =>
                {
                    app.Run(async context =>
                    {
                        var headers = context.Request.Headers
                            .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                        await _requestCapture.Writer.WriteAsync(new CapturedRequest(headers));
                        context.Response.StatusCode = 200;
                        await context.Response.WriteAsync("Hello from upstream");
                    });
                });
            })
            .Build();
    }

    private IHost BuildProxyHost(IReadOnlyActorRegistry registry)
    {
        var actorSystem = _actorSystem;
        return new HostBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
                web.ConfigureServices(services =>
                {
                    services.AddSingleton(actorSystem);
                    services.AddSingleton<IReadOnlyActorRegistry>(registry);
                    services.AddSingleton<IRequiredActor<DomainRouterActor>>(
                        new RequiredActor<DomainRouterActor>(registry));
                    services.AddHttpClient();
                    services.AddTurboHttpClient();
                    services.AddSingleton<RequestForwardingPipeline>();
                    services.AddSingleton<HeaderManipulationFilter>();
                    services.AddSingleton<WebSocketTunnel>();
                    services.AddSingleton<IProxyDispatcher, ProxyDispatcher>();
                    services.AddRouting();
                });
                web.Configure(app =>
                {
                    app.UseWebSockets();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFallback(async (HttpContext ctx, IProxyDispatcher dispatcher, CancellationToken ct) =>
                            await dispatcher.HandleAsync(ctx, ct));
                    });
                });
            })
            .Build();
    }

    private static int GetServerPort(IHost host)
    {
        var addressesFeature = host.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!;
        return new Uri(addressesFeature.Addresses.First()).Port;
    }

    private static RouteDefinition CreateRoute(string domain, string upstream)
    {
        var config = new DomainConfig { DomainName = DomainName.Parse(domain), ForceHttps = false };
        return RouteDefinition.Create(config, [UpstreamTarget.Create(upstream)]);
    }

    private sealed record CapturedRequest(Dictionary<string, string> Headers);
}
