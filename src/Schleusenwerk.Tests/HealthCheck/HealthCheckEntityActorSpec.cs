using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.HealthCheck;

public sealed class HealthCheckEntityActorSpec : TestKit
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private readonly ActorRegistry _registry;

    public HealthCheckEntityActorSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private IActorRef CreateActor(
        string url = "http://backend:8080",
        IHttpClientFactory? httpClientFactory = null,
        HealthCheckConfig? config = null)
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var target = new UpstreamTarget
        {
            Url = UpstreamUrl.Parse(url),
            HealthCheck = config ?? new HealthCheckConfig
            {
                Interval = TimeSpan.FromMilliseconds(50),
                UnhealthyThreshold = 3,
                HealthyThreshold = 2,
                Timeout = TimeSpan.FromSeconds(2),
            },
        };

        httpClientFactory ??= new AlwaysSucceedHttpClientFactory();

        return Sys.ActorOf(
            Props.Create(() => new HealthCheckEntityActor(target, httpClientFactory)),
            $"hc-{Guid.NewGuid():N}");
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckEntityActor_should_start_healthy()
    {
        var actor = CreateActor();

        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout, TestContext.Current.CancellationToken);

        Assert.True(status.IsHealthy);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckEntityActor_should_accept_subscriber_and_notify_on_health_change()
    {
        var actor = CreateActor(httpClientFactory: new AlwaysFailHttpClientFactory());
        var probe = CreateTestProbe();

        actor.Tell(new SubscribeHealth(probe) { Url = "http://backend:8080/" });

        var msg = probe.ExpectMsg<UpstreamHealthChanged>(TimeSpan.FromSeconds(3));
        Assert.False(msg.IsHealthy);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckEntityActor_should_remove_subscriber_on_unsubscribe()
    {
        var actor = CreateActor(httpClientFactory: new AlwaysFailHttpClientFactory(),
            config: new HealthCheckConfig
            {
                Interval = TimeSpan.FromSeconds(10),
                UnhealthyThreshold = 1,
                HealthyThreshold = 1,
                Timeout = TimeSpan.FromSeconds(2),
            });
        var probe = CreateTestProbe();

        actor.Tell(new SubscribeHealth(probe) { Url = "http://backend:8080/" });
        actor.Tell(new UnsubscribeHealth(probe) { Url = "http://backend:8080/" });

        // Trigger a manual check — subscriber should not receive the event
        actor.Tell(CheckHealth.Instance);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(Timeout = 10000)]
    public async Task HealthCheckEntityActor_should_remove_terminated_subscriber()
    {
        var actor = CreateActor(httpClientFactory: new AlwaysFailHttpClientFactory(),
            config: new HealthCheckConfig
            {
                Interval = TimeSpan.FromSeconds(10),
                UnhealthyThreshold = 1,
                HealthyThreshold = 1,
                Timeout = TimeSpan.FromSeconds(2),
            });

        var subscriberActor = Sys.ActorOf(Props.Create<BlackHoleActor>());
        actor.Tell(new SubscribeHealth(subscriberActor) { Url = "http://backend:8080/" });
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Sys.Stop(subscriberActor);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Actor should not crash when broadcasting after subscriber terminated
        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout, TestContext.Current.CancellationToken);
        Assert.NotNull(status);
    }

    private sealed class BlackHoleActor : ReceiveActor
    {
        public BlackHoleActor() { ReceiveAny(_ => { }); }
    }

    private sealed class AlwaysSucceedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new FakeHandler(true));
    }

    private sealed class AlwaysFailHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new FakeHandler(false));
    }

    private sealed class FakeHandler(bool succeed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(
                succeed ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.ServiceUnavailable));
    }
}
