using Akka.Actor;
using Akka.Hosting;
using Akka.Streams;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.HealthCheck;

public sealed class HealthCheckActorSpec : TestKit
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private readonly ActorRegistry _registry;

    public HealthCheckActorSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private static UpstreamTarget CreateTarget(
        string url = "http://backend:8080",
        HealthCheckConfig? config = null)
    {
        return new UpstreamTarget
        {
            Url = UpstreamUrl.Parse(url),
            Weight = 1,
            MaxConnections = 100,
            HealthCheck = config ?? FastConfig(),
        };
    }

    private static HealthCheckConfig FastConfig(
        int unhealthyThreshold = 3,
        int healthyThreshold = 2) =>
        new()
        {
            Interval = TimeSpan.FromMilliseconds(50),
            UnhealthyThreshold = unhealthyThreshold,
            HealthyThreshold = healthyThreshold,
            HealthEndpoint = "/health",
            Timeout = TimeSpan.FromSeconds(2),
        };

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly bool _alwaysSucceed;
        private readonly Func<bool>? _getResult;

        public FakeHttpClientFactory(bool alwaysSucceed)
        {
            _alwaysSucceed = alwaysSucceed;
        }

        public FakeHttpClientFactory(Func<bool> getResult)
        {
            _getResult = getResult;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FakeHandler(_getResult?.Invoke() ?? _alwaysSucceed));
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly bool _succeed;

        public FakeHandler(bool succeed)
        {
            _succeed = succeed;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(
                _succeed
                    ? System.Net.HttpStatusCode.OK
                    : System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }

    [Fact(Timeout = 10000)]
    public async Task HealthCheckActor_should_start_healthy()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var target = CreateTarget();
        var factory = new FakeHttpClientFactory(alwaysSucceed: true);
        var actor = Sys.ActorOf(Props.Create(() => new HealthCheckActor(target, factory)));

        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);

        Assert.True(status.IsHealthy);
        Assert.Equal(0, status.ConsecutiveFailures);
    }

    [Fact(Timeout = 10000)]
    public async Task HealthCheckActor_should_report_correct_consecutive_counts()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var config = FastConfig(unhealthyThreshold: 10);
        var target = CreateTarget(config: config);
        var factory = new FakeHttpClientFactory(alwaysSucceed: false);
        var actor = Sys.ActorOf(Props.Create(() => new HealthCheckActor(target, factory)));

        await Task.Delay(300);

        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);

        Assert.True(status.ConsecutiveFailures > 0);
        Assert.Equal(0, status.ConsecutiveSuccesses);
    }

    [Fact(Timeout = 10000)]
    public async Task HealthCheckActor_should_publish_unhealthy_event_after_threshold_failures()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var subscribed = await hub.Ask<EventHub.Subscribed>(
            EventHub.Subscribe.Instance,
            AskTimeout);

        var tcs = new TaskCompletionSource<IClusterEvent>();
        subscribed.SourceRef.Source
            .RunForeach(e => tcs.TrySetResult(e), Sys.Materializer());

        await Task.Delay(500);

        var config = FastConfig(unhealthyThreshold: 3);
        var target = CreateTarget(config: config);
        var factory = new FakeHttpClientFactory(alwaysSucceed: false);
        Sys.ActorOf(Props.Create(() => new HealthCheckActor(target, factory)));

        var evt = await tcs.Task.WaitAsync(AskTimeout);

        Assert.IsType<UpstreamHealthChanged>(evt);
        var healthEvt = (UpstreamHealthChanged)evt;
        Assert.Equal(target.Url, healthEvt.Url);
        Assert.False(healthEvt.IsHealthy);
    }
}
