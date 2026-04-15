using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.HealthCheck;

public sealed class HealthCheckActorSpec : TestKit
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private readonly UpstreamUrl _url = UpstreamUrl.Parse("http://backend:8080");
    private readonly ActorRegistry _registry;

    public HealthCheckActorSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private void RegisterEventHub(IActorRef hub)
    {
        _registry.Register<EventHub>(hub, overwrite: true);
    }

    private static Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> AlwaysHealthy() =>
        (_, _, _, _) => Task.FromResult(true);

    private static Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> AlwaysUnhealthy() =>
        (_, _, _, _) => Task.FromResult(false);

    private static Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> SequenceProbe(params bool[] results)
    {
        var index = 0;
        return (_, _, _, _) =>
        {
            var result = index < results.Length ? results[index] : results[^1];
            index++;
            return Task.FromResult(result);
        };
    }

    private static Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> ThrowingProbe() =>
        (_, _, _, _) => Task.FromException<bool>(new HttpRequestException("Connection refused"));

    private HealthCheckConfig FastConfig(
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

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_start_healthy()
    {
        RegisterEventHub(ActorRefs.Nobody);
        var actor = Sys.ActorOf(
            Props.Create(() => new HealthCheckActor(_url, FastConfig(), AlwaysHealthy())));

        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);

        Assert.True(status.IsHealthy);
        Assert.Equal(0, status.ConsecutiveFailures);
        Assert.Equal(0, status.ConsecutiveSuccesses);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_publish_unhealthy_event_after_threshold_failures()
    {
        var tcs = new TaskCompletionSource<UpstreamHealthChanged>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventSubscriberActor<UpstreamHealthChanged>(tcs)));

        var config = FastConfig(unhealthyThreshold: 3);
        RegisterEventHub(subscriber);
        Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, AlwaysUnhealthy())));

        var evt = await tcs.Task.WaitAsync(AskTimeout);

        Assert.Equal(_url, evt.Url);
        Assert.False(evt.IsHealthy);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_publish_healthy_event_after_recovery()
    {
        // First become unhealthy, then recover
        // 3 failures (threshold=3) to go unhealthy, then 2 successes (threshold=2) to recover
        var probe = SequenceProbe(false, false, false, true, true);

        var events = new List<UpstreamHealthChanged>();
        var recoveryTcs = new TaskCompletionSource<UpstreamHealthChanged>();
        var subscriber = Sys.ActorOf(Props.Create(() => new CollectingSubscriberActor(events, recoveryTcs)));

        var config = FastConfig(unhealthyThreshold: 3, healthyThreshold: 2);
        RegisterEventHub(subscriber);
        Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, probe)));

        var recoveryEvt = await recoveryTcs.Task.WaitAsync(AskTimeout);

        Assert.True(recoveryEvt.IsHealthy);
        Assert.Equal(2, events.Count);
        Assert.False(events[0].IsHealthy);
        Assert.True(events[1].IsHealthy);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_not_publish_event_before_threshold()
    {
        // Only 2 failures with threshold of 3 — should NOT publish
        var probe = SequenceProbe(false, false, true, true, true);

        var tcs = new TaskCompletionSource<UpstreamHealthChanged>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventSubscriberActor<UpstreamHealthChanged>(tcs)));

        var config = FastConfig(unhealthyThreshold: 3);
        RegisterEventHub(subscriber);
        var actor = Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, probe)));

        // Wait enough time for several checks
        await Task.Delay(500);

        // Verify no event published and still healthy
        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);
        Assert.True(status.IsHealthy);
        Assert.False(tcs.Task.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_treat_exceptions_as_failures()
    {
        var tcs = new TaskCompletionSource<UpstreamHealthChanged>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventSubscriberActor<UpstreamHealthChanged>(tcs)));

        var config = FastConfig(unhealthyThreshold: 2);
        RegisterEventHub(subscriber);
        Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, ThrowingProbe())));

        var evt = await tcs.Task.WaitAsync(AskTimeout);

        Assert.False(evt.IsHealthy);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_reset_failure_counter_on_success()
    {
        // Alternate: fail, fail, success, fail, fail — threshold 3 never reached
        var probe = SequenceProbe(false, false, true, false, false, true, true);

        var tcs = new TaskCompletionSource<UpstreamHealthChanged>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventSubscriberActor<UpstreamHealthChanged>(tcs)));

        var config = FastConfig(unhealthyThreshold: 3);
        RegisterEventHub(subscriber);
        var actor = Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, probe)));

        // Wait for checks to process
        await Task.Delay(700);

        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);
        Assert.True(status.IsHealthy);
        Assert.False(tcs.Task.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_report_correct_consecutive_counts()
    {
        // 2 consecutive failures
        var probe = SequenceProbe(false, false, false, false, false);

        var config = FastConfig(unhealthyThreshold: 10); // High threshold so we stay "healthy" by counter
        RegisterEventHub(ActorRefs.Nobody);
        var actor = Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, probe)));

        // Wait for a few checks
        await Task.Delay(300);

        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);
        Assert.True(status.ConsecutiveFailures > 0);
        Assert.Equal(0, status.ConsecutiveSuccesses);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_pass_correct_endpoint_to_probe()
    {
        string? capturedEndpoint = null;
        var probe = new Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>>(
            (_, endpoint, _, _) =>
            {
                capturedEndpoint = endpoint;
                return Task.FromResult(true);
            });

        var config = FastConfig() with { HealthEndpoint = "/custom/health" };
        RegisterEventHub(ActorRefs.Nobody);
        Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, probe)));

        // Wait for first check
        await Task.Delay(200);

        Assert.Equal("/custom/health", capturedEndpoint);
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_not_publish_duplicate_unhealthy_events()
    {
        var events = new List<UpstreamHealthChanged>();
        var firstUnhealthyTcs = new TaskCompletionSource<UpstreamHealthChanged>();
        var subscriber = Sys.ActorOf(Props.Create(() => new EventCollectorActor(events, firstUnhealthyTcs)));

        var config = FastConfig(unhealthyThreshold: 2);
        RegisterEventHub(subscriber);
        Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, AlwaysUnhealthy())));

        // Wait for first unhealthy event and several more checks
        await firstUnhealthyTcs.Task.WaitAsync(AskTimeout);
        await Task.Delay(500);

        // Should only have published one unhealthy event (not one per check)
        Assert.Single(events.Where(e => !e.IsHealthy));
    }

    [Fact(Timeout = 5000)]
    public async Task HealthCheckActor_should_use_configurable_interval()
    {
        var checkCount = 0;
        var probe = new Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>>(
            (_, _, _, _) =>
            {
                Interlocked.Increment(ref checkCount);
                return Task.FromResult(true);
            });

        var config = new HealthCheckConfig
        {
            Interval = TimeSpan.FromMilliseconds(100),
            UnhealthyThreshold = 3,
            HealthyThreshold = 2,
        };
        RegisterEventHub(ActorRefs.Nobody);
        Sys.ActorOf(Props.Create(() => new HealthCheckActor(_url, config, probe)));

        await Task.Delay(550);

        // At 100ms interval over 550ms, expect ~5 checks (allow some tolerance)
        Assert.InRange(checkCount, 3, 8);
    }

    private sealed class EventSubscriberActor<T> : ReceiveActor
    {
        public EventSubscriberActor(TaskCompletionSource<T> tcs)
        {
            Receive<T>(msg => tcs.TrySetResult(msg));
        }
    }

    private sealed class CollectingSubscriberActor : ReceiveActor
    {
        public CollectingSubscriberActor(
            List<UpstreamHealthChanged> events,
            TaskCompletionSource<UpstreamHealthChanged> recoveryTcs)
        {
            Receive<UpstreamHealthChanged>(msg =>
            {
                events.Add(msg);
                if (msg.IsHealthy)
                {
                    recoveryTcs.TrySetResult(msg);
                }
            });
        }
    }

    private sealed class EventCollectorActor : ReceiveActor
    {
        public EventCollectorActor(
            List<UpstreamHealthChanged> events,
            TaskCompletionSource<UpstreamHealthChanged> firstEventTcs)
        {
            Receive<UpstreamHealthChanged>(msg =>
            {
                events.Add(msg);
                firstEventTcs.TrySetResult(msg);
            });
        }
    }
}
