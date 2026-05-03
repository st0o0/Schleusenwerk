# Proxy Architecture Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the actor system to merge DomainEntity+UpstreamEntity, extract HealthChecks into their own ShardRegion, restrict EventHub to UI, and add circuit breaker, rate limiting, mTLS, and metrics.

**Architecture:** Three phases. Phase 1 rewires the actor graph (fusion, health shard, EventHub cleanup). Phase 2 adds resilience (circuit breaker, adaptive timeouts, rate limiting). Phase 3 adds operations (mTLS, OpenTelemetry metrics). Each phase is independently deployable.

**Tech Stack:** Akka.NET 1.5 (Akka.Hosting, Akka.Cluster.Sharding, Akka.Persistence.Sql), ASP.NET Core 9 (RateLimiting middleware, Kestrel mTLS), System.Diagnostics.Metrics, TurboHTTP, xUnit v3, Akka.TestKit/PersistenceTestKit.

---

## File Structure

### Phase 1 — Actor Redesign

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `src/Schleusenwerk/HealthCheck/HealthCheckEntityActor.cs` | Sharded health check actor with subscriber management |
| Create | `src/Schleusenwerk/HealthCheck/HealthCheckShardMessages.cs` | `SubscribeHealth`, `UnsubscribeHealth` messages with `IWithUrl` |
| Modify | `src/Schleusenwerk/Routing/DomainEntityActor.cs` | Remove UpstreamRegion dependency, add HealthCheck subscription |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs` | Remove upstream-pool shard, add health-check shard |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs` | Remove `IHealthCheckPropsFactory` registration |
| Delete | `src/Schleusenwerk/Routing/UpstreamEntityActor.cs` | Eliminated — merged into DomainEntityActor |
| Delete | `src/Schleusenwerk/Routing/UpstreamEntityEvents.cs` | Eliminated — `UpstreamConfigured` event no longer needed |
| Delete | `src/Schleusenwerk/HealthCheck/IHealthCheckPropsFactory.cs` | Eliminated — no longer spawning child actors |
| Delete | `src/Schleusenwerk/HealthCheck/HealthCheckPropsFactory.cs` | Eliminated — no longer spawning child actors |
| Modify | `src/Schleusenwerk/Routing/DomainRouterMessages.cs` | Remove `RegisterUpstream`, `SelectUpstreamForDomain` |
| Modify | `src/Schleusenwerk/HealthCheck/HealthCheckMessages.cs` | Make `CheckHealth` public (shard region needs it) |
| Create | `src/Schleusenwerk.Tests/HealthCheck/HealthCheckEntityActorSpec.cs` | Tests for new sharded health check actor |
| Modify | `src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs` | Remove UpstreamRegion probe, add HealthCheck region probe |
| Modify | `src/Schleusenwerk.Tests/Routing/DomainEntityActorHealthSpec.cs` | Update setup to remove UpstreamRegion probe |
| Delete | `src/Schleusenwerk.Tests/Routing/UpstreamEntityActorSpec.cs` | Eliminated with the actor |

### Phase 2 — Resilience

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `src/Schleusenwerk/Routing/UpstreamCircuitState.cs` | Circuit breaker state machine per upstream |
| Create | `src/Schleusenwerk/Routing/CircuitBreakerMessages.cs` | `RequestFailed`, `RequestSucceeded` messages |
| Modify | `src/Schleusenwerk/Routing/DomainEntityActor.cs` | Integrate circuit breaker into round-robin |
| Modify | `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs` | Configurable ask timeout, fire-and-forget success/failure Tell |
| Modify | `src/Schleusenwerk.Core/Routing/DomainConfig.cs` | Add `ConnectTimeout`, `CircuitBreakerCooldown`, `RateLimitConfig` |
| Create | `src/Schleusenwerk/RateLimiting/DomainRateLimitPolicy.cs` | Custom partition policy for ASP.NET RateLimiting |
| Create | `src/Schleusenwerk/RateLimiting/RateLimitConfigCache.cs` | In-memory cache fed by DomainEntityActor |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs` | Register rate limiting services |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs` | Add rate limiting middleware |
| Create | `src/Schleusenwerk.Tests/Routing/CircuitBreakerSpec.cs` | Circuit breaker state machine tests |
| Create | `src/Schleusenwerk.Tests/Routing/DomainEntityActorCircuitBreakerSpec.cs` | Integration with DomainEntityActor |
| Create | `src/Schleusenwerk.Tests/RateLimiting/DomainRateLimitPolicySpec.cs` | Rate limiting tests |

### Phase 3 — Operations & Security

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `src/Schleusenwerk/Security/ManagementPortCertificateValidator.cs` | mTLS client certificate validation |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs` | Configure dual Kestrel endpoints with mTLS |
| Create | `src/Schleusenwerk/Metrics/ProxyMetrics.cs` | Meter + all instrument definitions |
| Modify | `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs` | Instrument request counter and duration |
| Modify | `src/Schleusenwerk/Routing/DomainEntityActor.cs` | Instrument health and circuit breaker metrics |
| Create | `src/Schleusenwerk.Tests/Security/ManagementPortCertificateValidatorSpec.cs` | mTLS validation tests |
| Create | `src/Schleusenwerk.Tests/Metrics/ProxyMetricsSpec.cs` | Metrics emission tests |

---

## Phase 1: Actor Redesign

### Task 1: Create HealthCheck shard messages

**Files:**
- Create: `src/Schleusenwerk/HealthCheck/HealthCheckShardMessages.cs`
- Modify: `src/Schleusenwerk/HealthCheck/HealthCheckMessages.cs`

- [ ] **Step 1: Create HealthCheckShardMessages.cs**

```csharp
// src/Schleusenwerk/HealthCheck/HealthCheckShardMessages.cs
using Akka.Actor;
using Schleusenwerk.Routing;

namespace Schleusenwerk.HealthCheck;

public sealed record SubscribeHealth(IActorRef Subscriber) : IWithUrl
{
    public required string Url { get; init; }
}

public sealed record UnsubscribeHealth(IActorRef Subscriber) : IWithUrl
{
    public required string Url { get; init; }
}
```

- [ ] **Step 2: Make CheckHealth public in HealthCheckMessages.cs**

In `src/Schleusenwerk/HealthCheck/HealthCheckMessages.cs`, change `internal sealed record CheckHealth` to `public sealed record CheckHealth`. The shard region needs to deliver timer ticks to the entity.

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk/HealthCheck/HealthCheckShardMessages.cs src/Schleusenwerk/HealthCheck/HealthCheckMessages.cs
git commit -m "feat: add SubscribeHealth/UnsubscribeHealth messages for HealthCheck ShardRegion"
```

---

### Task 2: Create HealthCheckEntityActor

**Files:**
- Create: `src/Schleusenwerk/HealthCheck/HealthCheckEntityActor.cs`
- Create: `src/Schleusenwerk.Tests/HealthCheck/HealthCheckEntityActorSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/Schleusenwerk.Tests/HealthCheck/HealthCheckEntityActorSpec.cs
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

        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);

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
        await Task.Delay(500);

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
        await Task.Delay(100);

        Sys.Stop(subscriberActor);
        await Task.Delay(500);

        // Actor should not crash when broadcasting after subscriber terminated
        var status = await actor.Ask<HealthStatus>(GetHealthStatus.Instance, AskTimeout);
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.HealthCheck.HealthCheckEntityActorSpec"`
Expected: FAIL — `HealthCheckEntityActor` does not exist yet.

- [ ] **Step 3: Write HealthCheckEntityActor implementation**

```csharp
// src/Schleusenwerk/HealthCheck/HealthCheckEntityActor.cs
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;

namespace Schleusenwerk.HealthCheck;

public sealed class HealthCheckEntityActor : ReceiveActor, IWithTimers, IWithUnboundedStash
{
    private const string TimerKey = "health-check-tick";

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly UpstreamTarget _target;
    private readonly HealthCheckConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IActorRef _eventHub;
    private readonly HashSet<IActorRef> _subscribers = [];

    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;
    private IMaterializer _materializer = null!;

    private bool _isHealthy = true;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;

    public ITimerScheduler Timers { get; set; } = null!;
    public IStash Stash { get; set; } = null!;

    public HealthCheckEntityActor(UpstreamTarget target, IHttpClientFactory httpClientFactory)
    {
        _target = target;
        _config = target.HealthCheck;
        _httpClientFactory = httpClientFactory;
        _eventHub = Context.GetActor<EventHub>();

        WaitingForPublisher();
    }

    protected override void PreStart()
    {
        _materializer = Context.System.Materializer();
        _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
            .PipeTo(Self);
    }

    private void WaitingForPublisher()
    {
        Receive<EventHub.PublisherReady>(msg =>
        {
            var sink = msg.SinkRef.Sink;
            _publishQueue = Source.Queue<IClusterEvent>(100, OverflowStrategy.DropHead)
                .To(sink)
                .Run(_materializer);

            Timers.StartPeriodicTimer(TimerKey, CheckHealth.Instance, _config.Interval, _config.Interval);

            _log.Info("HealthCheckEntity publisher ready for {Url}", _target.Url);
            Stash.UnstashAll();
            Become(Idle);
        });
        Receive<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "Failed to get publisher from EventHub — retrying");
            _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
                .PipeTo(Self);
        });
        ReceiveAny(_ => Stash.Stash());
    }

    private void Idle()
    {
        Receive<CheckHealth>(_ => OnCheckHealth());
        Receive<GetHealthStatus>(_ => OnGetHealthStatus());
        Receive<SubscribeHealth>(OnSubscribe);
        Receive<UnsubscribeHealth>(OnUnsubscribe);
        Receive<Terminated>(OnTerminated);
    }

    private void Probing()
    {
        Receive<bool>(HandleProbeResult);
        Receive<CheckHealth>(_ => { });
        Receive<GetHealthStatus>(_ => OnGetHealthStatus());
        Receive<SubscribeHealth>(OnSubscribe);
        Receive<UnsubscribeHealth>(OnUnsubscribe);
        Receive<Terminated>(OnTerminated);
    }

    private void OnSubscribe(SubscribeHealth msg)
    {
        if (_subscribers.Add(msg.Subscriber))
        {
            Context.Watch(msg.Subscriber);
        }
    }

    private void OnUnsubscribe(UnsubscribeHealth msg)
    {
        if (_subscribers.Remove(msg.Subscriber))
        {
            Context.Unwatch(msg.Subscriber);
        }
    }

    private void OnTerminated(Terminated msg)
    {
        _subscribers.Remove(msg.ActorRef);
    }

    private void OnCheckHealth()
    {
        var self = Self;
        var url = _target.Url;
        var endpoint = _config.HealthEndpoint;
        var timeout = _config.Timeout;

        Task.Run(async () =>
        {
            using var client = _httpClientFactory.CreateClient("health-check");
            client.Timeout = timeout;
            try
            {
                var uri = new Uri($"{url}{endpoint.TrimStart('/')}");
                using var response = await client.GetAsync(uri);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }).PipeTo(self);

        Become(Probing);
    }

    private void HandleProbeResult(bool success)
    {
        if (success)
        {
            _consecutiveFailures = 0;
            _consecutiveSuccesses++;

            if (!_isHealthy && _consecutiveSuccesses >= _config.HealthyThreshold)
            {
                _isHealthy = true;
                _log.Info("Upstream {Url} is now healthy after {Count} consecutive successes",
                    _target.Url, _consecutiveSuccesses);
                var evt = new UpstreamHealthChanged(_target.Url, IsHealthy: true);
                NotifySubscribers(evt);
                PublishToEventHub(evt);
            }
        }
        else
        {
            _consecutiveSuccesses = 0;
            _consecutiveFailures++;

            if (_isHealthy && _consecutiveFailures >= _config.UnhealthyThreshold)
            {
                _isHealthy = false;
                _log.Warning("Upstream {Url} is now unhealthy after {Count} consecutive failures",
                    _target.Url, _consecutiveFailures);
                var evt = new UpstreamHealthChanged(_target.Url, IsHealthy: false);
                NotifySubscribers(evt);
                PublishToEventHub(evt);
            }
        }

        Become(Idle);
    }

    private void NotifySubscribers(UpstreamHealthChanged evt)
    {
        foreach (var subscriber in _subscribers)
        {
            subscriber.Tell(evt);
        }
    }

    private void OnGetHealthStatus()
    {
        Sender.Tell(new HealthStatus(_target.Url, _isHealthy, _consecutiveFailures, _consecutiveSuccesses));
    }

    private void PublishToEventHub(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Done.Instance,
            failure: ex => new PublishFailed(ex));
    }

    private sealed record PublishDropped(IClusterEvent Event);
    private sealed record PublishFailed(Exception Exception);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.HealthCheck.HealthCheckEntityActorSpec"`
Expected: All 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/HealthCheck/HealthCheckEntityActor.cs src/Schleusenwerk.Tests/HealthCheck/HealthCheckEntityActorSpec.cs
git commit -m "feat: add HealthCheckEntityActor as shardable health check with subscriber management"
```

---

### Task 3: Update DomainEntityActor — remove UpstreamRegion, add HealthCheck subscription

**Files:**
- Modify: `src/Schleusenwerk/Routing/DomainEntityActor.cs`
- Modify: `src/Schleusenwerk/Routing/DomainRouterMessages.cs`

- [ ] **Step 1: Remove `RegisterUpstream` and `SelectUpstreamForDomain` from DomainRouterMessages.cs**

In `src/Schleusenwerk/Routing/DomainRouterMessages.cs`, delete the `RegisterUpstream` and `SelectUpstreamForDomain` records. Keep `ResolveUpstream`, `UpstreamResolved`, `UpstreamNotFound`, `RoutesUpdated`, `RouteRemoved`.

The file should become:

```csharp
namespace Schleusenwerk.Routing;

public sealed record ResolveUpstream(string Host) : IWithDomain
{
    public string Domain => Host.ToLowerInvariant();
}

public sealed record UpstreamResolved(UpstreamTarget Target, DomainConfig Config);

public sealed record UpstreamNotFound(string Host);

public sealed record RoutesUpdated(IReadOnlyList<DomainName> Domains);

public sealed record RouteRemoved(DomainName DomainName);
```

- [ ] **Step 2: Rewrite DomainEntityActor to remove UpstreamRegion dependency and add HealthCheck subscription**

Replace `src/Schleusenwerk/Routing/DomainEntityActor.cs` with:

```csharp
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Routing;

public sealed class DomainEntityActor : ReceivePersistentActor, IWithUnboundedStash
{
    public override string PersistenceId => $"domain-{Self.Path.Name}";

    public new IStash Stash { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _healthCheckRegion;
    private readonly IActorRef _eventHub;
    private readonly IConfigurationStore _configStore;
    private IMaterializer _materializer = null!;
    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;

    private DomainConfig? _config;
    private readonly List<UpstreamTarget> _upstreamTargets = [];
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private int _roundRobinIndex;

    public DomainEntityActor(IConfigurationStore configStore)
    {
        _configStore = configStore;
        _healthCheckRegion = Context.GetActor<HealthCheckEntityActor>();
        _eventHub = Context.GetActor<EventHub>();

        Recover<DomainConfigured>(evt => _config = evt.Config);
        Recover<DomainUpstreamAdded>(evt => _upstreamTargets.Add(evt.Target));
        Recover<DomainUpstreamRemoved>(evt => _upstreamTargets.RemoveAll(t => t.Url.Equals(evt.Url)));
        Recover<DomainDeactivated>(_ =>
        {
            _config = null;
            _upstreamTargets.Clear();
        });
        Recover<SnapshotOffer>(_ => { });

        WaitingForPublisher();
    }

    protected override void PreStart()
    {
        base.PreStart();
        _materializer = Context.System.Materializer();
        _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
            .PipeTo(Self);
    }

    private void WaitingForPublisher()
    {
        Command<EventHub.PublisherReady>(msg =>
        {
            var sink = msg.SinkRef.Sink;
            _publishQueue = Source.Queue<IClusterEvent>(100, OverflowStrategy.DropHead)
                .To(sink)
                .Run(_materializer);

            _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe<IDomainEvent>.Instance)
                .PipeTo(Self);

            SubscribeToHealthChecks();

            Become(Ready);
            Stash.UnstashAll();
        });
        Command<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "Failed to get publisher from EventHub — retrying");
            _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
                .PipeTo(Self);
        });
        CommandAny(_ => Stash.Stash());
    }

    private void Ready()
    {
        Command<EventHub.Subscribed>(msg =>
        {
            msg.SourceRef.Source.RunWith(Sink.ActorRef<IClusterEvent>(Self, StreamCompleted.Instance, ex => new StreamFailed(ex)), _materializer);
        });
        Command<AddDomain>(HandleAddDomain);
        Command<UpdateDomain>(HandleUpdateDomain);
        Command<RemoveDomain>(HandleRemoveDomain);
        Command<AddUpstream>(HandleAddUpstream);
        Command<RemoveUpstream>(HandleRemoveUpstream);
        Command<GetDomainConfig>(_ => HandleGetConfig());
        Command<GetDomainUpstreamHealth>(_ => HandleGetUpstreamHealth());
        Command<ResolveUpstream>(HandleResolveUpstream);
        Command<UpstreamHealthChanged>(msg =>
        {
            if (msg.IsHealthy)
            {
                _unhealthyUrls.Remove(msg.Url);
            }
            else
            {
                _unhealthyUrls.Add(msg.Url);
            }
        });
        Command<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "Stream or async operation failed");
        });
        Command<StreamCompleted>(_ => { });
        Command<StreamFailed>(f =>
        {
            _log.Warning(f.Ex, "Event subscription stream failed");
        });
        Command<PublishDropped>(_ => { });
        Command<PublishFailed>(f =>
        {
            _log.Warning(f.Exception, "Failed to publish event to hub");
        });
        Command<IDomainEvent>(_ => { });
    }

    private void SubscribeToHealthChecks()
    {
        foreach (var upstream in _upstreamTargets)
        {
            _healthCheckRegion.Tell(new SubscribeHealth(Self) { Url = upstream.Url.Value.ToString() });
        }
    }

    private void HandleAddDomain(AddDomain cmd)
    {
        if (_config is not null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.Config.DomainName}' already configured."));
            return;
        }

        var evt = new DomainConfigured(cmd.Config);
        Persist(evt, persisted =>
        {
            _config = persisted.Config;
            _configStore.UpsertDomainAsync(persisted.Config);
            PublishEvent(persisted);
            PublishEvent(new CertificateProvisioningRequested(cmd.Config.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleUpdateDomain(UpdateDomain cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.Config.DomainName}' does not exist."));
            return;
        }

        var evt = new DomainConfigured(cmd.Config);
        Persist(evt, persisted =>
        {
            _config = persisted.Config;
            _configStore.UpsertDomainAsync(persisted.Config);
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleRemoveDomain(RemoveDomain cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.DomainName}' does not exist."));
            return;
        }

        foreach (var upstream in _upstreamTargets)
        {
            _healthCheckRegion.Tell(new UnsubscribeHealth(Self) { Url = upstream.Url.Value.ToString() });
        }

        var evt = new DomainDeactivated(cmd.DomainName);
        Persist(evt, persisted =>
        {
            _config = null;
            _upstreamTargets.Clear();
            _configStore.RemoveDomainAsync(cmd.DomainName);
            PublishEvent(persisted);
            Context.System.EventStream.Publish(new RouteRemoved(cmd.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
            Self.Tell(PoisonPill.Instance);
        });
    }

    private void HandleAddUpstream(AddUpstream cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack("Domain not configured yet."));
            return;
        }

        if (_upstreamTargets.Any(t => t.Url.Equals(cmd.Upstream.Url)))
        {
            Sender.Tell(new ConfigurationCommandNack($"Upstream '{cmd.Upstream.Url}' already exists."));
            return;
        }

        var evt = new DomainUpstreamAdded(cmd.Upstream);
        Persist(evt, persisted =>
        {
            _upstreamTargets.Add(persisted.Target);
            _healthCheckRegion.Tell(new SubscribeHealth(Self) { Url = persisted.Target.Url.Value.ToString() });
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleRemoveUpstream(RemoveUpstream cmd)
    {
        if (!_upstreamTargets.Any(t => t.Url.Equals(cmd.UpstreamUrl)))
        {
            Sender.Tell(new ConfigurationCommandNack($"Upstream '{cmd.UpstreamUrl}' does not exist."));
            return;
        }

        var evt = new DomainUpstreamRemoved(cmd.UpstreamUrl);
        Persist(evt, persisted =>
        {
            _upstreamTargets.RemoveAll(t => t.Url.Equals(persisted.Url));
            _unhealthyUrls.Remove(persisted.Url);
            _healthCheckRegion.Tell(new UnsubscribeHealth(Self) { Url = persisted.Url.Value.ToString() });
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleGetConfig()
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack("Domain not configured."));
            return;
        }

        Sender.Tell(new DomainConfigResult(_config, _upstreamTargets));
    }

    private void HandleGetUpstreamHealth()
    {
        var entries = _upstreamTargets
            .Select(t => new UpstreamHealthStatus(t.Url, !_unhealthyUrls.Contains(t.Url)))
            .ToList();
        Sender.Tell(new DomainUpstreamHealthResult(entries));
    }

    private void HandleResolveUpstream(ResolveUpstream msg)
    {
        if (_config is null)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var healthy = _upstreamTargets.Where(u => !_unhealthyUrls.Contains(u.Url)).ToList();
        if (healthy.Count == 0)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var picked = healthy[_roundRobinIndex % healthy.Count];
        _roundRobinIndex++;
        Sender.Tell(new UpstreamResolved(picked, _config));
    }

    private void PublishEvent(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Done.Instance,
            failure: ex => new PublishFailed(ex));
    }

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Ex);

    private sealed record PublishDropped(IClusterEvent Event);
    private sealed record PublishFailed(Exception Exception);
}
```

Key changes from the original:
- `_upstreamRegion` replaced with `_healthCheckRegion` (resolved via `Context.GetActor<HealthCheckEntityActor>()`)
- `RegisterUpstream` Tells removed — upstreams are managed inline
- `SubscribeToHealthChecks()` called after publisher ready — subscribes for each recovered upstream
- `AddUpstream` handler sends `SubscribeHealth` to health check region
- `RemoveUpstream` handler sends `UnsubscribeHealth` to health check region
- `RemoveDomain` handler unsubscribes all upstreams before deactivation

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx`
Expected: Build errors for `UpstreamEntityActor` references in tests and setup — expected, fixed in next tasks.

- [ ] **Step 4: Commit (WIP)**

```bash
git add src/Schleusenwerk/Routing/DomainEntityActor.cs src/Schleusenwerk/Routing/DomainRouterMessages.cs
git commit -m "feat: DomainEntityActor owns upstream state, subscribes to HealthCheck ShardRegion"
```

---

### Task 4: Update SchleusenwerkActorSystemSetup — swap shard regions

**Files:**
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`

- [ ] **Step 1: Replace upstream-pool with health-check shard region in SchleusenwerkActorSystemSetup.cs**

Replace the entire `BuildSystem` method body:

```csharp
protected override void BuildSystem(AkkaConfigurationBuilder builder, IServiceProvider serviceProvider)
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = configuration["Akka:Persistence:ConnectionString"] ?? "Data Source=/data/schleusenwerk.db";
    var hostname = configuration["Akka:Remoting:Hostname"] ?? "127.0.0.1";
    var port = int.TryParse(configuration["Akka:Remoting:Port"], out var p) ? p : 2552;

    builder.WithSqlPersistence(connectionString, ProviderName.SQLiteMS);

    builder.WithRemoting(hostname, port);
    builder.WithClustering(new ClusterOptions
    {
        Roles = ["schleusenwerk"],
        SeedNodes = [$"akka.tcp://schleusenwerk@{hostname}:{port}"]
    });

    var messageExtractor = HashCodeMessageExtractor.Create(
        maxNumberOfShards: 20,
        entityIdExtractor: msg => (msg as IWithEntityId)?.EntityId);

    var configStore = serviceProvider.GetRequiredService<IConfigurationStore>();

    builder.WithShardRegion<DomainEntityActor>(
        "domain-router",
        entityId => Props.Create(() => new DomainEntityActor(configStore)),
        messageExtractor,
        new ShardOptions
        {
            PassivateIdleEntityAfter = TimeSpan.FromMinutes(5),
            RememberEntities = true
        });

    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

    builder.WithShardRegion<HealthCheckEntityActor>(
        "health-check",
        entityId =>
        {
            var url = UpstreamUrl.Parse(Uri.UnescapeDataString(entityId));
            var target = new UpstreamTarget { Url = url };
            return Props.Create(() => new HealthCheckEntityActor(target, httpClientFactory));
        },
        messageExtractor,
        new ShardOptions
        {
            PassivateIdleEntityAfter = TimeSpan.FromMinutes(10),
            RememberEntities = false
        });

    builder.WithActors((system, registry, resolver) =>
    {
        var eventHub = system.ActorOf(resolver.Props<EventHub>(), "eventHub");
        registry.Register<EventHub>(eventHub);

        var dockerDiscovery = system.ActorOf(resolver.Props<DockerDiscoveryActor>(), "docker-discovery");
        registry.Register<DockerDiscoveryActor>(dockerDiscovery);

        var certProvisioning = system.ActorOf(resolver.Props<CertificateProvisioningActor>(), "cert-provisioning");
        registry.Register<CertificateProvisioningActor>(certProvisioning);
    });
}
```

Key changes:
- Removed `UpstreamEntityActor` shard region entirely
- Removed `IHealthCheckPropsFactory` usage
- Added `HealthCheckEntityActor` shard region with URL-based entity creation
- Health check shard uses 10-minute passivation and no entity remembering (transient state)

- [ ] **Step 2: Update usings in SchleusenwerkActorSystemSetup.cs**

Add `using Schleusenwerk.HealthCheck;` to the top. Remove `using Schleusenwerk.HealthCheck;` if it was only used for `IHealthCheckPropsFactory` (it's now used for `HealthCheckEntityActor`).

- [ ] **Step 3: Remove IHealthCheckPropsFactory from SchleusenwerkServicesSetup.cs**

In `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`, remove this line:
```csharp
services.AddSingleton<IHealthCheckPropsFactory, HealthCheckPropsFactory>();
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx`
Expected: May still fail on test project references to deleted types — fixed in Task 5.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs
git commit -m "feat: replace upstream-pool shard with health-check shard region"
```

---

### Task 5: Delete obsolete files and fix remaining references

**Files:**
- Delete: `src/Schleusenwerk/Routing/UpstreamEntityActor.cs`
- Delete: `src/Schleusenwerk/Routing/UpstreamEntityEvents.cs`
- Delete: `src/Schleusenwerk/HealthCheck/IHealthCheckPropsFactory.cs`
- Delete: `src/Schleusenwerk/HealthCheck/HealthCheckPropsFactory.cs`
- Delete: `src/Schleusenwerk.Tests/Routing/UpstreamEntityActorSpec.cs`
- Modify: `src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs`
- Modify: `src/Schleusenwerk.Tests/Routing/DomainEntityActorHealthSpec.cs`

- [ ] **Step 1: Delete obsolete source files**

```bash
rm src/Schleusenwerk/Routing/UpstreamEntityActor.cs
rm src/Schleusenwerk/Routing/UpstreamEntityEvents.cs
rm src/Schleusenwerk/HealthCheck/IHealthCheckPropsFactory.cs
rm src/Schleusenwerk/HealthCheck/HealthCheckPropsFactory.cs
rm src/Schleusenwerk.Tests/Routing/UpstreamEntityActorSpec.cs
```

- [ ] **Step 2: Update DomainEntityActorSpec.cs — replace UpstreamRegion probe with HealthCheck probe**

Replace the `CreateEntity` method:

```csharp
private (IActorRef entity, IActorRef healthCheckProbe) CreateEntity()
{
    var id = Interlocked.Increment(ref _actorCounter);
    var registry = ActorRegistry.For(Sys);

    var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{id}");
    registry.Register<EventHub>(hub, overwrite: true);

    var healthCheckProbe = CreateTestProbe();
    registry.Register<HealthCheckEntityActor>(healthCheckProbe, overwrite: true);

    var store = new SqliteConfigurationStore($"Data Source=test-{id}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    var entity = Sys.ActorOf(
        Props.Create(() => new DomainEntityActor(store)),
        $"domain-{id:D4}");
    return (entity, healthCheckProbe);
}
```

Update using directives: keep `using Schleusenwerk.HealthCheck;`, remove any `UpstreamEntityActor` references. All existing tests use `(entity, _)` pattern so the variable name change from `upstreamProbe` to `healthCheckProbe` doesn't affect them.

- [ ] **Step 3: Update DomainEntityActorHealthSpec.cs — replace UpstreamRegion probe with HealthCheck probe**

Replace the `CreateEntity` method:

```csharp
private IActorRef CreateEntity(string domain)
{
    var registry = ActorRegistry.For(Sys);
    var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-health-{Guid.NewGuid():N}");
    registry.Register<EventHub>(hub, overwrite: true);

    var healthCheckProbe = CreateTestProbe();
    registry.Register<HealthCheckEntityActor>(healthCheckProbe, overwrite: true);

    var store = new SqliteConfigurationStore(
        $"Data Source=health-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    return Sys.ActorOf(
        Props.Create(() => new DomainEntityActor(store)),
        $"entity-health-{Guid.NewGuid():N}");
}
```

- [ ] **Step 4: Search for any remaining references to deleted types**

Run: `grep -r "UpstreamEntityActor\|IHealthCheckPropsFactory\|HealthCheckPropsFactory\|RegisterUpstream\|SelectUpstreamForDomain\|UpstreamConfigured" src/ --include="*.cs"`

Fix any remaining references. Expected places:
- `src/Schleusenwerk/HealthCheck/HealthCheckActor.cs` — this is the old actor, keep for now (will be deleted when HealthCheckEntityActor fully replaces it, but it has no references to deleted types)

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx && dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: Build succeeds. All tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: remove UpstreamEntityActor and associated types, update test setup"
```

---

### Task 6: Delete old HealthCheckActor

**Files:**
- Delete: `src/Schleusenwerk/HealthCheck/HealthCheckActor.cs`
- Modify: `src/Schleusenwerk.Tests/HealthCheck/HealthCheckActorSpec.cs` (rename to test new actor)

- [ ] **Step 1: Delete old HealthCheckActor.cs**

```bash
rm src/Schleusenwerk/HealthCheck/HealthCheckActor.cs
```

- [ ] **Step 2: Update HealthCheckActorSpec.cs to test HealthCheckEntityActor**

Rename the file to reflect that it now tests `HealthCheckEntityActor`. Update the class to use `HealthCheckEntityActor` instead of `HealthCheckActor`:

In `src/Schleusenwerk.Tests/HealthCheck/HealthCheckActorSpec.cs`, replace every `new HealthCheckActor(` with `new HealthCheckEntityActor(`. Update the class name from `HealthCheckActorSpec` to `HealthCheckEntityActorLegacySpec` (or merge into `HealthCheckEntityActorSpec` from Task 2 if preferred).

Alternatively, delete `HealthCheckActorSpec.cs` if the tests in `HealthCheckEntityActorSpec.cs` from Task 2 already cover the same scenarios (start healthy, report consecutive counts, publish unhealthy event after threshold).

- [ ] **Step 3: Build and run all tests**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx && dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: Build succeeds. All tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: remove old HealthCheckActor, HealthCheckEntityActor is the replacement"
```

---

### Task 7: Phase 1 verification — end-to-end build and test

**Files:** None (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 3: Grep for dead references**

Run: `grep -r "UpstreamEntityActor\|IHealthCheckPropsFactory\|HealthCheckPropsFactory\|HealthCheckActor[^E]" src/ --include="*.cs"`
Expected: No matches in non-test, non-doc files (ProxyDispatcherSpec.cs is `#if false` so it's fine).

- [ ] **Step 4: Commit tag**

```bash
git tag phase-1-actor-redesign
```

---

## Phase 2: Resilience

### Task 8: Create UpstreamCircuitState

**Files:**
- Create: `src/Schleusenwerk/Routing/UpstreamCircuitState.cs`
- Create: `src/Schleusenwerk/Routing/CircuitBreakerMessages.cs`
- Create: `src/Schleusenwerk.Tests/Routing/CircuitBreakerSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/Schleusenwerk.Tests/Routing/CircuitBreakerSpec.cs
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class CircuitBreakerSpec
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_start_closed()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);

        Assert.Equal(CircuitStatus.Closed, state.Status);
        Assert.True(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_open_after_threshold_failures()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);

        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();

        Assert.Equal(CircuitStatus.Open, state.Status);
        Assert.False(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_reset_failure_count_on_success()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);

        state.RecordFailure();
        state.RecordFailure();
        state.RecordSuccess();

        Assert.Equal(CircuitStatus.Closed, state.Status);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_transition_to_half_open_after_cooldown()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), TimeSpan.FromMilliseconds(50));

        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();

        Assert.Equal(CircuitStatus.Open, state.Status);

        Thread.Sleep(100);

        Assert.Equal(CircuitStatus.HalfOpen, state.Status);
        Assert.True(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_close_on_success_when_half_open()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), TimeSpan.FromMilliseconds(50));

        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();

        Thread.Sleep(100);
        Assert.Equal(CircuitStatus.HalfOpen, state.Status);

        state.RecordSuccess();
        Assert.Equal(CircuitStatus.Closed, state.Status);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_reopen_with_doubled_cooldown_on_failure_when_half_open()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), TimeSpan.FromMilliseconds(50));

        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();

        Thread.Sleep(100);
        Assert.Equal(CircuitStatus.HalfOpen, state.Status);

        state.RecordFailure();
        Assert.Equal(CircuitStatus.Open, state.Status);

        // Should NOT be half-open after 50ms anymore (doubled to 100ms)
        Thread.Sleep(60);
        Assert.Equal(CircuitStatus.Open, state.Status);

        Thread.Sleep(60);
        Assert.Equal(CircuitStatus.HalfOpen, state.Status);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_force_open_on_health_check_failure()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);

        state.ForceOpen();

        Assert.Equal(CircuitStatus.Open, state.Status);
        Assert.False(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_force_close_on_health_check_recovery()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);

        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();
        Assert.Equal(CircuitStatus.Open, state.Status);

        state.ForceClose();

        Assert.Equal(CircuitStatus.Closed, state.Status);
        Assert.True(state.IsAvailable);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.CircuitBreakerSpec"`
Expected: FAIL — `UpstreamCircuitState` does not exist yet.

- [ ] **Step 3: Implement UpstreamCircuitState**

```csharp
// src/Schleusenwerk/Routing/UpstreamCircuitState.cs
namespace Schleusenwerk.Routing;

public enum CircuitStatus
{
    Closed,
    Open,
    HalfOpen,
}

public sealed class UpstreamCircuitState
{
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);
    private const int FailureThreshold = 3;

    private readonly UpstreamUrl _url;
    private readonly TimeSpan _baseCooldown;
    private CircuitStatus _status = CircuitStatus.Closed;
    private DateTime _openedAt;
    private TimeSpan _currentCooldown;

    public UpstreamCircuitState(UpstreamUrl url, TimeSpan baseCooldown)
    {
        _url = url;
        _baseCooldown = baseCooldown;
        _currentCooldown = baseCooldown;
    }

    public int ConsecutiveFailures { get; private set; }

    public CircuitStatus Status
    {
        get
        {
            if (_status == CircuitStatus.Open && DateTime.UtcNow - _openedAt >= _currentCooldown)
            {
                _status = CircuitStatus.HalfOpen;
            }
            return _status;
        }
    }

    public bool IsAvailable => Status != CircuitStatus.Open;

    public void RecordFailure()
    {
        if (Status == CircuitStatus.HalfOpen)
        {
            _currentCooldown = TimeSpan.FromTicks(Math.Min(_currentCooldown.Ticks * 2, MaxCooldown.Ticks));
            TransitionToOpen();
            return;
        }

        ConsecutiveFailures++;
        if (ConsecutiveFailures >= FailureThreshold)
        {
            _currentCooldown = _baseCooldown;
            TransitionToOpen();
        }
    }

    public void RecordSuccess()
    {
        _status = CircuitStatus.Closed;
        ConsecutiveFailures = 0;
        _currentCooldown = _baseCooldown;
    }

    public void ForceOpen()
    {
        _currentCooldown = _baseCooldown;
        TransitionToOpen();
    }

    public void ForceClose()
    {
        _status = CircuitStatus.Closed;
        ConsecutiveFailures = 0;
        _currentCooldown = _baseCooldown;
    }

    private void TransitionToOpen()
    {
        _status = CircuitStatus.Open;
        _openedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Create CircuitBreakerMessages.cs**

```csharp
// src/Schleusenwerk/Routing/CircuitBreakerMessages.cs
namespace Schleusenwerk.Routing;

public sealed record RequestFailed(UpstreamUrl Url) : IWithDomain
{
    public required string Domain { get; init; }
}

public sealed record RequestSucceeded(UpstreamUrl Url) : IWithDomain
{
    public required string Domain { get; init; }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.CircuitBreakerSpec"`
Expected: All 8 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk/Routing/UpstreamCircuitState.cs src/Schleusenwerk/Routing/CircuitBreakerMessages.cs src/Schleusenwerk.Tests/Routing/CircuitBreakerSpec.cs
git commit -m "feat: add UpstreamCircuitState with Closed/Open/HalfOpen transitions"
```

---

### Task 9: Integrate circuit breaker into DomainEntityActor

**Files:**
- Modify: `src/Schleusenwerk/Routing/DomainEntityActor.cs`
- Create: `src/Schleusenwerk.Tests/Routing/DomainEntityActorCircuitBreakerSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/Schleusenwerk.Tests/Routing/DomainEntityActorCircuitBreakerSpec.cs
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainEntityActorCircuitBreakerSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private IActorRef CreateEntity()
    {
        var registry = ActorRegistry.For(Sys);
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-cb-{Guid.NewGuid():N}");
        registry.Register<EventHub>(hub, overwrite: true);

        var healthCheckProbe = CreateTestProbe();
        registry.Register<HealthCheckEntityActor>(healthCheckProbe, overwrite: true);

        var store = new SqliteConfigurationStore(
            $"Data Source=cb-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        return Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"entity-cb-{Guid.NewGuid():N}");
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_exclude_upstream_after_consecutive_request_failures()
    {
        var entity = CreateEntity();
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var upstream1 = UpstreamTarget.Create("http://a:8080");
        var upstream2 = UpstreamTarget.Create("http://b:9090");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream1), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream2), Timeout);

        // 3 failures on upstream1 → circuit opens
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });

        // Allow messages to process
        await Task.Delay(100);

        // Should only resolve to upstream2
        for (var i = 0; i < 4; i++)
        {
            var resolved = await entity.Ask<UpstreamResolved>(
                new ResolveUpstream("example.com"), Timeout);
            Assert.Equal("b", resolved.Target.Url.Value.Host);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reinclude_upstream_after_success_report()
    {
        var entity = CreateEntity();
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var upstream1 = UpstreamTarget.Create("http://a:8080");
        var upstream2 = UpstreamTarget.Create("http://b:9090");

        await entity.Ask<ConfigurationCommandAck>(new AddDomain(config), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream1), Timeout);
        await entity.Ask<ConfigurationCommandAck>(
            new AddUpstream(config.DomainName, upstream2), Timeout);

        // Open circuit on upstream1
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        entity.Tell(new RequestFailed(upstream1.Url) { Domain = "example.com" });
        await Task.Delay(100);

        // Health check says it's back
        entity.Tell(new UpstreamHealthChanged(upstream1.Url, IsHealthy: true));
        await Task.Delay(100);

        // Should resolve to both now
        var hosts = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var resolved = await entity.Ask<UpstreamResolved>(
                new ResolveUpstream("example.com"), Timeout);
            hosts.Add(resolved.Target.Url.Value.Host);
        }

        Assert.Contains("a", hosts);
        Assert.Contains("b", hosts);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.DomainEntityActorCircuitBreakerSpec"`
Expected: FAIL — `RequestFailed` message not handled, or upstream not excluded.

- [ ] **Step 3: Add circuit breaker state to DomainEntityActor**

In `src/Schleusenwerk/Routing/DomainEntityActor.cs`, add a field:

```csharp
private readonly Dictionary<UpstreamUrl, UpstreamCircuitState> _circuitStates = new();
```

Add a helper to get/create circuit state:

```csharp
private UpstreamCircuitState GetCircuitState(UpstreamUrl url)
{
    if (!_circuitStates.TryGetValue(url, out var state))
    {
        state = new UpstreamCircuitState(url, TimeSpan.FromSeconds(30));
        _circuitStates[url] = state;
    }
    return state;
}
```

In the `Ready()` method, add handlers:

```csharp
Command<RequestFailed>(msg =>
{
    var state = GetCircuitState(msg.Url);
    state.RecordFailure();
});
Command<RequestSucceeded>(msg =>
{
    var state = GetCircuitState(msg.Url);
    state.RecordSuccess();
});
```

Update the `UpstreamHealthChanged` handler:

```csharp
Command<UpstreamHealthChanged>(msg =>
{
    if (msg.IsHealthy)
    {
        _unhealthyUrls.Remove(msg.Url);
        if (_circuitStates.TryGetValue(msg.Url, out var state))
        {
            state.ForceClose();
        }
    }
    else
    {
        _unhealthyUrls.Add(msg.Url);
        GetCircuitState(msg.Url).ForceOpen();
    }
});
```

Update `HandleResolveUpstream` to use circuit state:

```csharp
private void HandleResolveUpstream(ResolveUpstream msg)
{
    if (_config is null)
    {
        Sender.Tell(new UpstreamNotFound(msg.Host));
        return;
    }

    var available = _upstreamTargets
        .Where(u => !_unhealthyUrls.Contains(u.Url) && GetCircuitState(u.Url).IsAvailable)
        .ToList();

    if (available.Count == 0)
    {
        Sender.Tell(new UpstreamNotFound(msg.Host));
        return;
    }

    var picked = available[_roundRobinIndex % available.Count];
    _roundRobinIndex++;
    Sender.Tell(new UpstreamResolved(picked, _config));
}
```

Clean up circuit state in `HandleRemoveUpstream`:

```csharp
// Inside the Persist callback, after _upstreamTargets.RemoveAll:
_circuitStates.Remove(persisted.Url);
```

And in `HandleRemoveDomain`, inside the Persist callback:

```csharp
_circuitStates.Clear();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.DomainEntityActorCircuitBreakerSpec"`
Expected: Both tests PASS.

- [ ] **Step 5: Run full test suite to check for regressions**

Run: `dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk/Routing/DomainEntityActor.cs src/Schleusenwerk.Tests/Routing/DomainEntityActorCircuitBreakerSpec.cs
git commit -m "feat: integrate circuit breaker into DomainEntityActor round-robin selection"
```

---

### Task 10: Adaptive timeouts in ProxyDispatcher and DomainConfig changes

**Files:**
- Modify: `src/Schleusenwerk.Core/Routing/DomainConfig.cs`
- Modify: `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs`

- [ ] **Step 1: Add ConnectTimeout and CircuitBreakerCooldown to DomainConfig**

```csharp
// src/Schleusenwerk.Core/Routing/DomainConfig.cs
namespace Schleusenwerk.Routing;

public sealed record DomainConfig
{
    public required DomainName DomainName { get; init; }
    public RedirectMode HttpRedirect { get; init; } = RedirectMode.None;
    public Uri? RedirectUrl { get; init; }
    public bool ForceHttps { get; init; }
    public bool PreserveHostHeader { get; init; } = true;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CircuitBreakerCooldown { get; init; } = TimeSpan.FromSeconds(30);
    public RateLimitConfig? RateLimit { get; init; }
}

public sealed record RateLimitConfig
{
    public int RequestsPerWindow { get; init; } = 100;
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(60);
}
```

- [ ] **Step 2: Update ProxyDispatcher — configurable ask timeout and fire-and-forget feedback**

```csharp
// src/Schleusenwerk/Forwarding/ProxyDispatcher.cs
using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Forwarding;

internal sealed class ProxyDispatcher : IProxyDispatcher
{
    private readonly IActorRef _domainRegion;
    private readonly RequestForwardingPipeline _pipeline;
    private readonly HeaderManipulationFilter _headerFilter;
    private readonly WebSocketTunnel _webSocketTunnel;
    private readonly TimeSpan _resolveTimeout;

    public ProxyDispatcher(
        IRequiredActor<DomainEntityActor> domainRegionProvider,
        RequestForwardingPipeline pipeline,
        HeaderManipulationFilter headerFilter,
        WebSocketTunnel webSocketTunnel,
        IConfiguration configuration)
    {
        _domainRegion = domainRegionProvider.ActorRef;
        _pipeline = pipeline;
        _headerFilter = headerFilter;
        _webSocketTunnel = webSocketTunnel;

        var seconds = double.TryParse(configuration["Proxy:ResolveTimeoutSeconds"], out var s) ? s : 3;
        _resolveTimeout = TimeSpan.FromSeconds(seconds);
    }

    public async Task HandleAsync(HttpContext context, CancellationToken ct)
    {
        var host = context.Request.Host.Host;

        if (string.IsNullOrEmpty(host))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var response = await _domainRegion.Ask<object>(
            new ResolveUpstream(host),
            _resolveTimeout,
            ct);

        switch (response)
        {
            case UpstreamResolved resolved:
                await HandleResolvedRoute(context, host, resolved.Target, resolved.Config, ct);
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
        string domain,
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

        var statusCode = context.Response.StatusCode;
        if (statusCode is >= 502 and <= 504)
        {
            _domainRegion.Tell(new RequestFailed(upstream.Url) { Domain = domain });
        }
        else
        {
            _domainRegion.Tell(new RequestSucceeded(upstream.Url) { Domain = domain });
        }
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
```

Key changes:
- `AskTimeout` from config instead of hardcoded 5s (default 3s)
- `IConfiguration` injected via constructor
- After `ForwardAsync`, fires `RequestFailed` or `RequestSucceeded` Tell to the domain region
- `HandleResolvedRoute` now takes `domain` parameter for the message routing

- [ ] **Step 3: Build and test**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx && dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: Build succeeds. Tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.Core/Routing/DomainConfig.cs src/Schleusenwerk/Forwarding/ProxyDispatcher.cs
git commit -m "feat: adaptive ask timeout, circuit breaker feedback from ProxyDispatcher"
```

---

### Task 11: Rate limiting middleware

**Files:**
- Create: `src/Schleusenwerk/RateLimiting/RateLimitConfigCache.cs`
- Create: `src/Schleusenwerk/RateLimiting/DomainRateLimitPolicy.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`
- Create: `src/Schleusenwerk.Tests/RateLimiting/DomainRateLimitPolicySpec.cs`

- [ ] **Step 1: Write the failing test for RateLimitConfigCache**

```csharp
// src/Schleusenwerk.Tests/RateLimiting/DomainRateLimitPolicySpec.cs
using Schleusenwerk.RateLimiting;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.RateLimiting;

public sealed class DomainRateLimitPolicySpec
{
    [Fact(Timeout = 5000)]
    public void RateLimitConfigCache_should_return_null_for_unknown_domain()
    {
        var cache = new RateLimitConfigCache();

        var config = cache.GetConfig("unknown.com");

        Assert.Null(config);
    }

    [Fact(Timeout = 5000)]
    public void RateLimitConfigCache_should_return_config_after_update()
    {
        var cache = new RateLimitConfigCache();
        var rateLimit = new RateLimitConfig { RequestsPerWindow = 50, Window = TimeSpan.FromSeconds(30) };

        cache.UpdateConfig("example.com", rateLimit);

        var config = cache.GetConfig("example.com");
        Assert.NotNull(config);
        Assert.Equal(50, config.RequestsPerWindow);
    }

    [Fact(Timeout = 5000)]
    public void RateLimitConfigCache_should_remove_config()
    {
        var cache = new RateLimitConfigCache();
        var rateLimit = new RateLimitConfig();

        cache.UpdateConfig("example.com", rateLimit);
        cache.RemoveConfig("example.com");

        Assert.Null(cache.GetConfig("example.com"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.RateLimiting.DomainRateLimitPolicySpec"`
Expected: FAIL — namespace/types don't exist.

- [ ] **Step 3: Implement RateLimitConfigCache**

```csharp
// src/Schleusenwerk/RateLimiting/RateLimitConfigCache.cs
using System.Collections.Concurrent;
using Schleusenwerk.Routing;

namespace Schleusenwerk.RateLimiting;

public sealed class RateLimitConfigCache
{
    private readonly ConcurrentDictionary<string, RateLimitConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

    public RateLimitConfig? GetConfig(string domain)
    {
        return _configs.GetValueOrDefault(domain);
    }

    public void UpdateConfig(string domain, RateLimitConfig config)
    {
        _configs[domain] = config;
    }

    public void RemoveConfig(string domain)
    {
        _configs.TryRemove(domain, out _);
    }
}
```

- [ ] **Step 4: Implement DomainRateLimitPolicy**

```csharp
// src/Schleusenwerk/RateLimiting/DomainRateLimitPolicy.cs
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Schleusenwerk.RateLimiting;

public static class DomainRateLimitPolicy
{
    public const string PolicyName = "per-client-per-domain";

    public static RateLimiterOptions ConfigurePolicy(this RateLimiterOptions options, RateLimitConfigCache cache)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(PolicyName, context =>
        {
            var host = context.Request.Host.Host;
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var config = cache.GetConfig(host);
            if (config is null)
            {
                return RateLimitPartition.GetNoLimiter($"{host}:{clientIp}");
            }

            return RateLimitPartition.GetSlidingWindowLimiter(
                $"{host}:{clientIp}",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = config.RequestsPerWindow,
                    Window = config.Window,
                    SegmentsPerWindow = 4,
                    AutoReplenishment = true,
                });
        });
        return options;
    }
}
```

- [ ] **Step 5: Register rate limiting in services and app pipeline**

In `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`, add:

```csharp
services.AddSingleton<RateLimitConfigCache>();
services.AddRateLimiter(options =>
{
    var cache = services.BuildServiceProvider().GetRequiredService<RateLimitConfigCache>();
    options.ConfigurePolicy(cache);
});
```

Add `using Schleusenwerk.RateLimiting;` to the usings.

In `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`, add `app.UseRateLimiter();` before the `app.MapFallback(...)` call. Add `.RequireRateLimiting(DomainRateLimitPolicy.PolicyName)` to the fallback endpoint:

```csharp
app.UseRateLimiter();

app.MapFallback(async (HttpContext ctx, IProxyDispatcher dispatcher, CancellationToken ct) =>
    await dispatcher.HandleAsync(ctx, ct))
    .RequireRateLimiting(DomainRateLimitPolicy.PolicyName);
```

Add `using Schleusenwerk.RateLimiting;` to the usings.

- [ ] **Step 6: Run tests**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.RateLimiting.DomainRateLimitPolicySpec"`
Expected: All 3 tests PASS.

- [ ] **Step 7: Build full solution**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add src/Schleusenwerk/RateLimiting/ src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs src/Schleusenwerk.Tests/RateLimiting/ src/Schleusenwerk.Core/Routing/DomainConfig.cs
git commit -m "feat: add per-client-per-domain rate limiting middleware"
```

---

### Task 12: Phase 2 verification

**Files:** None (verification only)

- [ ] **Step 1: Full build and test**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx && dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All pass.

- [ ] **Step 2: Tag**

```bash
git tag phase-2-resilience
```

---

## Phase 3: Operations & Security

### Task 13: mTLS on management port

**Files:**
- Create: `src/Schleusenwerk/Security/ManagementPortCertificateValidator.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`
- Create: `src/Schleusenwerk.Tests/Security/ManagementPortCertificateValidatorSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/Schleusenwerk.Tests/Security/ManagementPortCertificateValidatorSpec.cs
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Security;
using Xunit;

namespace Schleusenwerk.Tests.Security;

public sealed class ManagementPortCertificateValidatorSpec
{
    private static (X509Certificate2 ca, X509Certificate2 client) CreateTestCertificates()
    {
        using var caKey = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=TestCA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var ca = caReq.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));

        using var clientKey = RSA.Create(2048);
        var clientReq = new CertificateRequest("CN=TestClient", clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var serialNumber = new byte[20];
        RandomNumberGenerator.Fill(serialNumber);
        var client = clientReq.Create(ca, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1), serialNumber);
        client = client.CopyWithPrivateKey(clientKey);

        return (ca, client);
    }

    [Fact(Timeout = 5000)]
    public void Validator_should_accept_certificate_signed_by_trusted_ca()
    {
        var (ca, client) = CreateTestCertificates();
        var validator = new ManagementPortCertificateValidator(ca);

        var result = validator.Validate(client);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void Validator_should_reject_self_signed_certificate()
    {
        var (ca, _) = CreateTestCertificates();
        var validator = new ManagementPortCertificateValidator(ca);

        using var rogueKey = RSA.Create(2048);
        var rogueReq = new CertificateRequest("CN=Rogue", rogueKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var rogue = rogueReq.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));

        var result = validator.Validate(rogue);

        Assert.False(result);
    }

    [Fact(Timeout = 5000)]
    public void Validator_should_reject_null_certificate()
    {
        var (ca, _) = CreateTestCertificates();
        var validator = new ManagementPortCertificateValidator(ca);

        var result = validator.Validate(null);

        Assert.False(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Security.ManagementPortCertificateValidatorSpec"`
Expected: FAIL — `ManagementPortCertificateValidator` does not exist.

- [ ] **Step 3: Implement ManagementPortCertificateValidator**

```csharp
// src/Schleusenwerk/Security/ManagementPortCertificateValidator.cs
using System.Security.Cryptography.X509Certificates;

namespace Schleusenwerk.Security;

public sealed class ManagementPortCertificateValidator
{
    private readonly X509Certificate2 _trustedCa;

    public ManagementPortCertificateValidator(X509Certificate2 trustedCa)
    {
        _trustedCa = trustedCa;
    }

    public bool Validate(X509Certificate2? clientCertificate)
    {
        if (clientCertificate is null)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.CustomTrustStore.Add(_trustedCa);
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

        return chain.Build(clientCertificate);
    }
}
```

- [ ] **Step 4: Configure dual Kestrel endpoints in SchleusenwerkServicesSetup.cs**

Add the management port configuration inside `SetupServices`, within the existing `services.Configure<KestrelServerOptions>` block. Replace the current Kestrel configuration:

```csharp
services.Configure<KestrelServerOptions>(options =>
{
    var managementCaPath = configuration["Management:CaCertificatePath"];

    options.ConfigureHttpsDefaults(adapterOptions =>
    {
        var selector = options.ApplicationServices!.GetRequiredService<SniCertificateSelector>();
        adapterOptions.ServerCertificateSelector = (_, hostname) => selector.Select(hostname);
    });

    if (!string.IsNullOrEmpty(managementCaPath) && File.Exists(managementCaPath))
    {
        var managementPort = int.TryParse(configuration["Management:Port"], out var mp) ? mp : 5000;
        var caCert = X509CertificateLoader.LoadCertificateFromFile(managementCaPath);
        var validator = new ManagementPortCertificateValidator(caCert);

        options.ListenAnyIP(managementPort, listenOptions =>
        {
            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
                httpsOptions.ClientCertificateValidation = (cert, _, _) => validator.Validate(cert);
            });
        });
    }
});
```

Add usings:
```csharp
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Security;
```

- [ ] **Step 5: Run tests**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Security.ManagementPortCertificateValidatorSpec"`
Expected: All 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk/Security/ManagementPortCertificateValidator.cs src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs src/Schleusenwerk.Tests/Security/ManagementPortCertificateValidatorSpec.cs
git commit -m "feat: add mTLS client certificate validation for management port"
```

---

### Task 14: OpenTelemetry proxy metrics

**Files:**
- Create: `src/Schleusenwerk/Metrics/ProxyMetrics.cs`
- Modify: `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs`
- Modify: `src/Schleusenwerk/Routing/DomainEntityActor.cs`
- Create: `src/Schleusenwerk.Tests/Metrics/ProxyMetricsSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// src/Schleusenwerk.Tests/Metrics/ProxyMetricsSpec.cs
using System.Diagnostics.Metrics;
using Schleusenwerk.Metrics;
using Xunit;

namespace Schleusenwerk.Tests.Metrics;

public sealed class ProxyMetricsSpec
{
    [Fact(Timeout = 5000)]
    public void ProxyMetrics_should_expose_request_counter()
    {
        var metrics = new ProxyMetrics();
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "proxy.requests")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();

        metrics.RecordRequest("example.com", 200);
        listener.RecordObservableInstruments();

        Assert.Single(measurements);
        Assert.Equal(1, measurements[0]);
    }

    [Fact(Timeout = 5000)]
    public void ProxyMetrics_should_expose_duration_histogram()
    {
        var metrics = new ProxyMetrics();
        var measurements = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "proxy.request.duration")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();

        metrics.RecordDuration("example.com", "http://backend:8080", 150.5);

        Assert.Single(measurements);
        Assert.Equal(150.5, measurements[0]);
    }

    [Fact(Timeout = 5000)]
    public void ProxyMetrics_should_expose_rate_limit_rejected_counter()
    {
        var metrics = new ProxyMetrics();
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "proxy.rate_limit.rejected")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();

        metrics.RecordRateLimitRejected("example.com", "1.2.3.4");

        Assert.Single(measurements);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Metrics.ProxyMetricsSpec"`
Expected: FAIL — `ProxyMetrics` does not exist.

- [ ] **Step 3: Implement ProxyMetrics**

```csharp
// src/Schleusenwerk/Metrics/ProxyMetrics.cs
using System.Diagnostics.Metrics;

namespace Schleusenwerk.Metrics;

public sealed class ProxyMetrics
{
    private static readonly Meter Meter = new("Schleusenwerk.Proxy");

    private readonly Counter<long> _requestCounter = Meter.CreateCounter<long>("proxy.requests");
    private readonly Histogram<double> _requestDuration = Meter.CreateHistogram<double>("proxy.request.duration", "ms");
    private readonly Counter<long> _circuitBreakerTrips = Meter.CreateCounter<long>("proxy.circuit_breaker.trips");
    private readonly Counter<long> _rateLimitRejected = Meter.CreateCounter<long>("proxy.rate_limit.rejected");
    private readonly UpDownCounter<long> _upstreamHealth = Meter.CreateUpDownCounter<long>("proxy.upstream.health");

    public void RecordRequest(string domain, int statusCode)
    {
        _requestCounter.Add(1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("status_code", statusCode));
    }

    public void RecordDuration(string domain, string upstreamUrl, double durationMs)
    {
        _requestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("upstream_url", upstreamUrl));
    }

    public void RecordCircuitBreakerTrip(string domain, string upstreamUrl)
    {
        _circuitBreakerTrips.Add(1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("upstream_url", upstreamUrl));
    }

    public void RecordRateLimitRejected(string domain, string clientIp)
    {
        _rateLimitRejected.Add(1,
            new KeyValuePair<string, object?>("domain", domain),
            new KeyValuePair<string, object?>("client_ip", clientIp));
    }

    public void RecordUpstreamHealthChange(string upstreamUrl, bool isHealthy)
    {
        _upstreamHealth.Add(isHealthy ? 1 : -1,
            new KeyValuePair<string, object?>("upstream_url", upstreamUrl));
    }
}
```

- [ ] **Step 4: Wire ProxyMetrics into ProxyDispatcher**

In `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs`, add `ProxyMetrics` to constructor:

```csharp
private readonly ProxyMetrics _metrics;
```

Add to constructor parameters: `ProxyMetrics metrics` and assign `_metrics = metrics;`.

In `HandleAsync`, after the switch statement, add:

```csharp
_metrics.RecordRequest(host, context.Response.StatusCode);
```

In `HandleResolvedRoute`, wrap the `ForwardAsync` call with timing:

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
await _pipeline.ForwardAsync(context, upstream, config, _headerFilter);
sw.Stop();
_metrics.RecordDuration(domain, upstream.Url.Value.ToString(), sw.Elapsed.TotalMilliseconds);
```

Add `using Schleusenwerk.Metrics;` to the usings.

- [ ] **Step 5: Register ProxyMetrics in DI**

In `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`, add:

```csharp
services.AddSingleton<ProxyMetrics>();
```

Add `using Schleusenwerk.Metrics;` to the usings.

- [ ] **Step 6: Run tests**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Metrics.ProxyMetricsSpec"`
Expected: All 3 tests PASS.

- [ ] **Step 7: Full build and test**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx && dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All pass.

- [ ] **Step 8: Commit**

```bash
git add src/Schleusenwerk/Metrics/ProxyMetrics.cs src/Schleusenwerk/Forwarding/ProxyDispatcher.cs src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs src/Schleusenwerk.Tests/Metrics/ProxyMetricsSpec.cs
git commit -m "feat: add OpenTelemetry proxy metrics (requests, duration, circuit breaker, rate limit, health)"
```

---

### Task 15: Phase 3 verification and final tag

**Files:** None (verification only)

- [ ] **Step 1: Full clean build**

Run: `dotnet build --configuration Release .\src\Schleusenwerk.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 3: Final grep for dead references**

Run: `grep -r "UpstreamEntityActor\|IHealthCheckPropsFactory\|HealthCheckPropsFactory\|HealthCheckActor[^E]" src/ --include="*.cs" | grep -v "^#if false" | grep -v "ProxyDispatcherSpec"`
Expected: No matches.

- [ ] **Step 4: Tag**

```bash
git tag phase-3-operations
```
