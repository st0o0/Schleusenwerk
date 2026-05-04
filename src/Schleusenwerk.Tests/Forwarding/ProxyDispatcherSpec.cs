namespace Schleusenwerk.Tests.Forwarding;

/// <summary>
/// TODO: Task 6+ — Update to use DomainEntityActor query messages instead of ConfigurationPersistenceActor.
/// Tests expect GetDomainByName to be answered by ConfigurationPersistenceActor, but that actor
/// no longer handles domain/upstream commands. DomainEntityActor should answer GetDomainConfig instead.
/// </summary>
#if false
public sealed class ProxyDispatcherSpec : TestKit
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;

    public ProxyDispatcherSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private ProxyDispatcher CreateDispatcher(
        IActorRef domainRegion,
        RecordingTurboHttpClient? recordingClient = null)
    {
        var client = recordingClient ?? new RecordingTurboHttpClient();
        var factory = new StubTurboHttpClientFactory(client);
        var pipeline = new RequestForwardingPipeline(factory);
        var headerFilter = new HeaderManipulationFilter();
        var webSocketTunnel = new WebSocketTunnel();

        return new ProxyDispatcher(
            new RequiredActor<DomainEntityActor>(_registry),
            pipeline,
            headerFilter,
            webSocketTunnel);
    }

    private IActorRef CreateUpstreamRegionThatReplies(UpstreamTarget target, DomainConfig config)
    {
        return Sys.ActorOf(Props.Create(() => new ReplyingUpstreamActor(target, config)));
    }

    private (IActorRef domainRegion, IActorRef hub) CreateDomainRegion(IActorRef upstreamRegion)
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var configProbe = CreateTestProbe();
        _registry.Register<ConfigurationPersistenceActor>(configProbe, overwrite: true);

        _registry.Register<UpstreamEntityActor>(upstreamRegion, overwrite: true);

        var domainRegion = Sys.ActorOf(
            Props.Create<DomainEntityActor>(),
            $"domain-{Guid.NewGuid():N}");
        _registry.Register<DomainEntityActor>(domainRegion, overwrite: true);

        // Answer the initial config query
        configProbe.ExpectMsg<GetDomainByName>(TimeSpan.FromSeconds(3));
        var config = new DomainConfig { DomainName = DomainName.Parse("unknown") };
        configProbe.Reply(new DomainConfigResult(config, []));

        return (domainRegion, hub);
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
    public async Task Dispatcher_should_return_404_when_domain_has_no_upstreams()
    {
        var upstreamProbe = CreateTestProbe();
        var (domainRegion, _) = CreateDomainRegion(upstreamProbe);

        var config = new DomainConfig { DomainName = DomainName.Parse("empty.example.com") };
        domainRegion.Tell(new SetRoute(config, []));
        await Task.Delay(100);

        var dispatcher = CreateDispatcher(domainRegion);
        var context = CreateHttpContext("empty.example.com", "/test");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_forward_request_when_domain_is_configured()
    {
        var target = UpstreamTarget.Create("http://backend:8080");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        var upstreamRegion = CreateUpstreamRegionThatReplies(target, config);
        var (domainRegion, _) = CreateDomainRegion(upstreamRegion);

        domainRegion.Tell(new SetRoute(config, [target]));
        await Task.Delay(100);

        var recordingClient = new RecordingTurboHttpClient();
        var dispatcher = CreateDispatcher(domainRegion, recordingClient);
        var context = CreateHttpContext("example.com", "/api/data");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Single(recordingClient.SentRequests);
        Assert.Contains("backend", recordingClient.SentRequests[0].RequestUri!.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_redirect_http_to_https_with_301_when_configured()
    {
        var target = UpstreamTarget.Create("http://backend:8080");
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("secure.example.com"),
            HttpRedirect = RedirectMode.PermanentRedirect,
            ForceHttps = true
        };

        var upstreamRegion = CreateUpstreamRegionThatReplies(target, config);
        var (domainRegion, _) = CreateDomainRegion(upstreamRegion);

        domainRegion.Tell(new SetRoute(config, [target]));
        await Task.Delay(100);

        var dispatcher = CreateDispatcher(domainRegion);
        var context = CreateHttpContext("secure.example.com", "/page", queryString: "?q=1");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        Assert.Equal("https://secure.example.com/page?q=1", context.Response.Headers.Location.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_return_400_when_host_header_is_empty()
    {
        var upstreamProbe = CreateTestProbe();
        var (domainRegion, _) = CreateDomainRegion(upstreamProbe);

        var dispatcher = CreateDispatcher(domainRegion);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(string.Empty);
        context.Request.Path = "/test";
        context.Request.Method = "GET";

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    private sealed class ReplyingUpstreamActor : ReceiveActor
    {
        public ReplyingUpstreamActor(UpstreamTarget target, DomainConfig config)
        {
            Receive<SelectUpstreamForDomain>(_ => Sender.Tell(new UpstreamResolved(target, config)));
            Receive<RegisterUpstream>(_ => { });
        }
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

        public StubTurboHttpClientFactory(ITurboHttpClient client) => _client = client;

        public ITurboHttpClient CreateClient(string name) => _client;
    }
}
#endif
