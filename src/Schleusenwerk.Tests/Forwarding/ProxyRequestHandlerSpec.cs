using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http;
using Schleusenwerk.Forwarding;
using Schleusenwerk.LoadBalancing;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using TurboHTTP;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class ProxyRequestHandlerSpec : TestKit
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;

    public ProxyRequestHandlerSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private IActorRef CreateRouter()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var router = Sys.ActorOf(
            Props.Create(() => new DomainRouterActor(
                upstreams => Props.Create(() => new LoadBalancerActor(upstreams)))),
            $"router-{Guid.NewGuid():N}");
        _registry.Register<DomainRouterActor>(router, overwrite: true);

        return router;
    }

    private static RouteDefinition CreateRoute(
        string domain,
        RedirectMode redirect = RedirectMode.None,
        bool forceHttps = false,
        params string[] upstreams)
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse(domain),
            HttpRedirect = redirect,
            ForceHttps = forceHttps,
        };
        var targets = upstreams.Select(u => UpstreamTarget.Create(u)).ToList();
        return RouteDefinition.Create(config, targets);
    }

    private ProxyRequestHandler CreateHandler(
        IActorRef router,
        RecordingTurboHttpClient? recordingClient = null)
    {
        var client = recordingClient ?? new RecordingTurboHttpClient();
        var factory = new StubTurboHttpClientFactory(client);
        var pipeline = new RequestForwardingPipeline(factory);
        var headerFilter = new HeaderManipulationFilter();
        var webSocketTunnel = new WebSocketTunnel();

        return new ProxyRequestHandler(
            _ => Task.CompletedTask,
            new RequiredActor<DomainRouterActor>(_registry),
            pipeline,
            headerFilter,
            webSocketTunnel);
    }

    private static DefaultHttpContext CreateHttpContext(
        string host,
        string path = "/",
        string scheme = "http",
        string method = "GET",
        string? queryString = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.Method = method;
        if (queryString is not null)
        {
            context.Request.QueryString = new QueryString(queryString);
        }
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_return_404_when_domain_not_configured()
    {
        var router = CreateRouter();
        var handler = CreateHandler(router);

        var context = CreateHttpContext("unknown.example.com", "/test");

        await handler.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_forward_request_when_domain_is_configured()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var handler = CreateHandler(router, recordingClient);

        var context = CreateHttpContext("example.com", "/api/data");

        await handler.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Single(recordingClient.SentRequests);
        Assert.Contains("backend", recordingClient.SentRequests[0].RequestUri!.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_redirect_http_to_https_with_301_when_configured()
    {
        var router = CreateRouter();
        var route = CreateRoute("secure.example.com",
            redirect: RedirectMode.PermanentRedirect,
            forceHttps: true,
            upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("secure.example.com"), AskTimeout);

        var handler = CreateHandler(router);
        var context = CreateHttpContext("secure.example.com", "/page", queryString: "?q=1");

        await handler.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        Assert.Equal("https://secure.example.com/page?q=1", context.Response.Headers.Location.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_redirect_with_307_when_configured()
    {
        var router = CreateRouter();
        var route = CreateRoute("temp.example.com",
            redirect: RedirectMode.TemporaryRedirect,
            forceHttps: true,
            upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("temp.example.com"), AskTimeout);

        var handler = CreateHandler(router);
        var context = CreateHttpContext("temp.example.com", "/api");

        await handler.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_not_redirect_when_already_https()
    {
        var router = CreateRouter();
        var route = CreateRoute("secure.example.com",
            redirect: RedirectMode.PermanentRedirect,
            forceHttps: true,
            upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("secure.example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var handler = CreateHandler(router, recordingClient);
        var context = CreateHttpContext("secure.example.com", "/page", scheme: "https");

        await handler.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Single(recordingClient.SentRequests);
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_round_robin_across_multiple_upstreams()
    {
        var router = CreateRouter();
        var route = CreateRoute("lb.example.com",
            upstreams: ["http://a:8080", "http://b:8080", "http://c:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("lb.example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var handler = CreateHandler(router, recordingClient);

        for (var i = 0; i < 6; i++)
        {
            var context = CreateHttpContext("lb.example.com", "/test");
            await handler.InvokeAsync(context);
        }

        Assert.Equal(6, recordingClient.SentRequests.Count);

        var hosts = recordingClient.SentRequests.Select(r => r.RequestUri!.Host).ToList();
        Assert.Equal(2, hosts.Count(h => h == "a"));
        Assert.Equal(2, hosts.Count(h => h == "b"));
        Assert.Equal(2, hosts.Count(h => h == "c"));
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_apply_header_manipulation_filter()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient(additionalResponseHeaders: new Dictionary<string, string>
        {
            ["Server"] = "hidden-upstream",
            ["X-Powered-By"] = "SomeFramework",
        });
        var handler = CreateHandler(router, recordingClient);
        var context = CreateHttpContext("example.com", "/test");

        await handler.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Server"));
        Assert.False(context.Response.Headers.ContainsKey("X-Powered-By"));
        Assert.True(context.Response.Headers.ContainsKey("Via"));
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_return_400_when_host_header_is_empty()
    {
        var router = CreateRouter();
        var handler = CreateHandler(router);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(string.Empty);
        context.Request.Path = "/test";
        context.Request.Method = "GET";

        await handler.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Handler_should_forward_single_upstream_without_round_robin()
    {
        var router = CreateRouter();
        var route = CreateRoute("single.example.com", upstreams: ["http://only:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("single.example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var handler = CreateHandler(router, recordingClient);

        for (var i = 0; i < 3; i++)
        {
            var context = CreateHttpContext("single.example.com", "/test");
            await handler.InvokeAsync(context);
        }

        Assert.Equal(3, recordingClient.SentRequests.Count);
        Assert.All(recordingClient.SentRequests, r => Assert.Equal("only", r.RequestUri!.Host));
    }

    internal sealed class RecordingTurboHttpClient : ITurboHttpClient
    {
        private readonly Dictionary<string, string>? _additionalResponseHeaders;

        public RecordingTurboHttpClient(Dictionary<string, string>? additionalResponseHeaders = null)
        {
            _additionalResponseHeaders = additionalResponseHeaders;
        }

        public List<HttpRequestMessage> SentRequests { get; } = [];

        public Uri? BaseAddress { get; set; }
        public HttpRequestHeaders DefaultRequestHeaders => new HttpRequestMessage().Headers;
        public Version DefaultRequestVersion { get; set; } = HttpVersion.Version11;
        public HttpVersionPolicy DefaultVersionPolicy { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public long MaxResponseContentBufferSize { get; set; }
        public ChannelWriter<HttpRequestMessage> Requests => Channel.CreateUnbounded<HttpRequestMessage>().Writer;
        public ChannelReader<HttpResponseMessage> Responses => Channel.CreateUnbounded<HttpResponseMessage>().Reader;

        public void CancelPendingRequests() { }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (_additionalResponseHeaders is not null)
            {
                foreach (var header in _additionalResponseHeaders)
                {
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            return Task.FromResult(response);
        }

        public void Dispose() { }
    }

    private sealed class StubTurboHttpClientFactory : ITurboHttpClientFactory
    {
        private readonly ITurboHttpClient _client;

        public StubTurboHttpClientFactory(ITurboHttpClient client)
        {
            _client = client;
        }

        public ITurboHttpClient CreateClient(string name) => _client;
    }
}
