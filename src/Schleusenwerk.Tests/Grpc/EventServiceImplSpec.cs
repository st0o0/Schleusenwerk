using Akka.Actor;
using Akka.Hosting;
using Akka.Streams;
using Akka.TestKit.Xunit;
using Grpc.Core;
using Schleusenwerk.Contracts;
using Schleusenwerk.Grpc;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Grpc;

public sealed class EventServiceImplSpec : TestKit
{
    private EventServiceImpl CreateSut()
    {
        var registry = ActorRegistry.For(Sys);
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-event-{Guid.NewGuid():N}");
        registry.Register<EventHub>(hub, overwrite: true);
        return new EventServiceImpl(registry, Sys.Materializer());
    }

    [Fact(Timeout = 5000)]
    public async Task Subscribe_should_deliver_domain_configured_events()
    {
        var sut = CreateSut();
        var request = new SubscribeRequest { Filter = string.Empty };
        var eventHub = ActorRegistry.For(Sys).Get<EventHub>();
        var receivedEvents = new List<ProxyEvent>();
        var tcs = new TaskCompletionSource();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var mockStream = new MockServerStreamWriter(async evt =>
        {
            receivedEvents.Add(evt);
            if (receivedEvents.Count >= 1)
            {
                tcs.TrySetResult();
            }
        });

        var subscribeTask = sut.Subscribe(request, mockStream, new TestServerCallContext(cts.Token));

        await Task.Delay(50);

        eventHub.Tell(new DomainConfigured(new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            ForceHttps = true,
            RequestTimeout = TimeSpan.FromSeconds(30)
        }));

        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            // Event may take time to process
        }

        Assert.Single(receivedEvents);
        Assert.Equal(EventType.RouteUpdated, receivedEvents[0].Type);
        Assert.Equal("example.com", receivedEvents[0].Domain);
    }

    [Fact(Timeout = 5000)]
    public async Task Subscribe_should_filter_by_domain()
    {
        var sut = CreateSut();
        var request = new SubscribeRequest { Filter = "domain:example.com" };
        var eventHub = ActorRegistry.For(Sys).Get<EventHub>();
        var receivedEvents = new List<ProxyEvent>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var mockStream = new MockServerStreamWriter(async evt =>
        {
            receivedEvents.Add(evt);
        });

        var subscribeTask = sut.Subscribe(request, mockStream, new TestServerCallContext(cts.Token));

        await Task.Delay(50);

        eventHub.Tell(new DomainConfigured(new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            ForceHttps = true,
            RequestTimeout = TimeSpan.FromSeconds(30)
        }));

        eventHub.Tell(new DomainConfigured(new DomainConfig
        {
            DomainName = DomainName.Parse("other.com"),
            ForceHttps = true,
            RequestTimeout = TimeSpan.FromSeconds(30)
        }));

        await Task.Delay(200);

        Assert.Single(receivedEvents);
        Assert.Equal("example.com", receivedEvents[0].Domain);
    }

    [Fact(Timeout = 5000)]
    public async Task Subscribe_should_handle_wildcard_filter()
    {
        var sut = CreateSut();
        var request = new SubscribeRequest { Filter = "*" };
        var eventHub = ActorRegistry.For(Sys).Get<EventHub>();
        var receivedEvents = new List<ProxyEvent>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var mockStream = new MockServerStreamWriter(async evt =>
        {
            receivedEvents.Add(evt);
        });

        var subscribeTask = sut.Subscribe(request, mockStream, new TestServerCallContext(cts.Token));

        await Task.Delay(50);

        eventHub.Tell(new DomainConfigured(new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            ForceHttps = true,
            RequestTimeout = TimeSpan.FromSeconds(30)
        }));

        eventHub.Tell(new DomainDeactivated(DomainName.Parse("other.com")));

        await Task.Delay(200);

        Assert.Equal(2, receivedEvents.Count);
        Assert.Equal(EventType.RouteUpdated, receivedEvents[0].Type);
        Assert.Equal(EventType.RouteRemoved, receivedEvents[1].Type);
    }

    private sealed class MockServerStreamWriter : IServerStreamWriter<ProxyEvent>
    {
        private readonly Func<ProxyEvent, Task> _onWrite;

        public WriteOptions? WriteOptions { get; set; }

        public MockServerStreamWriter(Func<ProxyEvent, Task> onWrite)
        {
            _onWrite = onWrite;
        }

        public async Task WriteAsync(ProxyEvent message)
        {
            await _onWrite(message);
        }
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        private readonly CancellationToken _cancellationToken;

        public TestServerCallContext(CancellationToken cancellationToken = default)
        {
            _cancellationToken = cancellationToken;
        }

        protected override string MethodCore => "Test";
        protected override string HostCore => "localhost";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => _cancellationToken;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override global::Grpc.Core.Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("", []);
        protected override string PeerCore => "127.0.0.1";

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
