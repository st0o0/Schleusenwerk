# Domain Router Cluster Sharding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `DomainRouterActor` + `LoadBalancerActor` with two Akka.Cluster.Sharding regions (`DomainEntityActor` per domain, `UpstreamEntityActor` per URL) to eliminate the routing bottleneck under high domain count.

**Architecture:** `DomainEntityActor` subscribes to `EventHub` via `StreamRef`, tracks healthy upstreams with round-robin, and forwards `SelectUpstreamForDomain` to `UpstreamEntityActor` preserving the original sender — no `Ask`, no double round-trip. `UpstreamEntityActor` owns a `HealthCheckActor` child and replies `UpstreamResolved` directly to `ProxyRequestHandler`. `ConfigurationPersistenceActor` sends `SetRoute` per domain after recovery and after each state mutation.

**Tech Stack:** `Akka.Cluster.Sharding`, `Akka.Cluster.Hosting`, `Akka.Hosting` `WithShardRegion<T>`, Akka.Streams `StreamRef`, xUnit v3

---

## File Map

| Action | File | Responsibility |
|---|---|---|
| Modify | `src/Schleusenwerk/Routing/DomainRouterMessages.cs` | Add `IWithDomain`/`IWithUrl` interfaces + `SetRoute`, `RegisterUpstream`, `SelectUpstreamForDomain`; add interfaces to existing messages; remove `UpdateRoutes` |
| Create | `src/Schleusenwerk/Routing/DomainEntityActor.cs` | Per-domain shard entity — round-robin, health tracking, forward to upstream region |
| Create | `src/Schleusenwerk/Routing/UpstreamEntityActor.cs` | Per-URL shard entity — owns `HealthCheckActor` child, replies `UpstreamResolved` |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs` | Replace `DomainRouterActor` with two `WithShardRegion` calls; add `WithClustering`/`WithRemoting` |
| Modify | `src/Schleusenwerk/Persistence/ConfigurationPersistenceActor.cs` | Add `_domainRegion`; send `SetRoute`/routing `RemoveDomain` on mutations and on recovery completion |
| Modify | `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs` | Switch `IRequiredActor<DomainRouterActor>` → `IRequiredActor<DomainEntityActor>` |
| Modify | `src/Directory.Packages.props` | Add `Akka.Cluster.Hosting` version |
| Modify | `src/Schleusenwerk/Schleusenwerk.csproj` | Add `Akka.Cluster.Hosting` package reference |
| Create | `src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs` | Unit tests for `DomainEntityActor` routing behaviour |
| Create | `src/Schleusenwerk.Tests/Routing/DomainEntityHealthSpec.cs` | Unit tests for `DomainEntityActor` health tracking |
| Create | `src/Schleusenwerk.Tests/Routing/UpstreamEntityActorSpec.cs` | Unit tests for `UpstreamEntityActor` |
| Delete | `src/Schleusenwerk/Routing/DomainRouterActor.cs` | Replaced by sharding |
| Delete | `src/Schleusenwerk/LoadBalancing/LoadBalancerActor.cs` | Replaced by `DomainEntityActor` + `UpstreamEntityActor` |
| Delete | `src/Schleusenwerk/LoadBalancing/UpstreamRouteeActor.cs` | Replaced by `UpstreamEntityActor` |
| Delete | `src/Schleusenwerk/LoadBalancing/Messages.cs` | All messages replaced |
| Delete | `src/Schleusenwerk.Tests/Routing/DomainRouterActorSpec.cs` | Replaced by `DomainEntityActorSpec` |
| Delete | `src/Schleusenwerk.Tests/Routing/DomainRouterHealthSpec.cs` | Replaced by `DomainEntityHealthSpec` |
| Delete | `src/Schleusenwerk.Tests/LoadBalancing/LoadBalancerActorSpec.cs` | Actor deleted |

---

## Task 1: Message Protocol

**Files:**
- Modify: `src/Schleusenwerk/Routing/DomainRouterMessages.cs`

After this task the project will not build (files referencing `UpdateRoutes`/`DomainRouterActor` break) — that's expected and resolved in subsequent tasks.

- [ ] **Step 1: Rewrite `DomainRouterMessages.cs`**

Replace the entire file with:

```csharp
namespace Schleusenwerk.Routing;

public interface IWithDomain
{
    string Domain { get; }
}

public interface IWithUrl
{
    string Url { get; }
}

/// <summary>
/// Sets or replaces the route for a single domain. Sent by ConfigurationPersistenceActor.
/// </summary>
public sealed record SetRoute(DomainConfig Config, IReadOnlyList<UpstreamTarget> Upstreams) : IWithDomain
{
    public string Domain => Config.DomainName.Value;
}

/// <summary>
/// Registers an upstream URL with its entity actor. Sent by DomainEntityActor on SetRoute.
/// </summary>
public sealed record RegisterUpstream(UpstreamTarget Target) : IWithUrl
{
    public string Url => Target.Url.Value.ToString();
}

/// <summary>
/// Forwarded from DomainEntityActor to UpstreamEntityActor, preserving the original sender.
/// </summary>
public sealed record SelectUpstreamForDomain(DomainConfig Config, string Url) : IWithUrl;

/// <summary>
/// Resolves the upstream route for a given host header value.
/// </summary>
public sealed record ResolveUpstream(string Host) : IWithDomain
{
    public string Domain => Host.ToLowerInvariant();
}

/// <summary>
/// Removes a domain from the routing table.
/// </summary>
public sealed record RemoveDomain(DomainName DomainName) : IWithDomain
{
    public string Domain => DomainName.Value;
}

/// <summary>
/// Result of a successful upstream resolution.
/// </summary>
public sealed record UpstreamResolved(UpstreamTarget Target, DomainConfig Config);

/// <summary>
/// Result when no upstream is found for the requested host.
/// </summary>
public sealed record UpstreamNotFound(string Host);

/// <summary>
/// Published to EventStream when routes are added or updated.
/// </summary>
public sealed record RoutesUpdated(IReadOnlyList<DomainName> Domains);

/// <summary>
/// Published to EventStream when a domain is removed.
/// </summary>
public sealed record RouteRemoved(DomainName DomainName);
```

- [ ] **Step 2: Commit**

```bash
git add src/Schleusenwerk/Routing/DomainRouterMessages.cs
git commit -m "feat(routing): add IWithDomain/IWithUrl interfaces and sharding message protocol"
```

---

## Task 2: UpstreamEntityActor

**Files:**
- Create: `src/Schleusenwerk/Routing/UpstreamEntityActor.cs`
- Create: `src/Schleusenwerk.Tests/Routing/UpstreamEntityActorSpec.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Schleusenwerk.Tests/Routing/UpstreamEntityActorSpec.cs`:

```csharp
using Akka.Actor;
using Akka.TestKit.Xunit;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class UpstreamEntityActorSpec : TestKit
{
    private IActorRef CreateEntity(IActorRef? healthCheckProbe = null)
    {
        var probe = healthCheckProbe ?? CreateTestProbe();
        Func<UpstreamTarget, Props> factory = _ => Props.Create<NullActor>();
        if (healthCheckProbe != null)
        {
            factory = _ => Props.Create(() => new ForwardingActor(healthCheckProbe));
        }
        return Sys.ActorOf(Props.Create(() => new UpstreamEntityActor(factory)));
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_reply_UpstreamResolved_on_SelectUpstreamForDomain()
    {
        var target = UpstreamTarget.Create("http://upstream:8080");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var entity = CreateEntity();

        entity.Tell(new RegisterUpstream(target));
        entity.Tell(new SelectUpstreamForDomain(config, "http://upstream:8080/"));

        var resolved = ExpectMsg<UpstreamResolved>();
        Assert.Equal("upstream", resolved.Target.Url.Host);
        Assert.Equal("example.com", resolved.Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_update_target_on_second_RegisterUpstream()
    {
        var first = UpstreamTarget.Create("http://v1:8080");
        var second = UpstreamTarget.Create("http://v2:9090");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };
        var entity = CreateEntity();

        entity.Tell(new RegisterUpstream(first));
        entity.Tell(new RegisterUpstream(second));
        entity.Tell(new SelectUpstreamForDomain(config, "http://v2:9090/"));

        var resolved = ExpectMsg<UpstreamResolved>();
        Assert.Equal("v2", resolved.Target.Url.Host);
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_start_health_check_actor_on_first_RegisterUpstream()
    {
        var healthProbe = CreateTestProbe();
        var target = UpstreamTarget.Create("http://upstream:8080");
        var entity = Sys.ActorOf(Props.Create(() => new UpstreamEntityActor(
            _ => Props.Create(() => new ForwardingActor(healthProbe)))));

        entity.Tell(new RegisterUpstream(target));

        healthProbe.ExpectMsg<RegisterUpstream>(TimeSpan.FromSeconds(1));
    }

    [Fact(Timeout = 5000)]
    public void UpstreamEntityActor_should_not_start_second_health_check_on_re_register()
    {
        var startCount = 0;
        var target = UpstreamTarget.Create("http://upstream:8080");
        var entity = Sys.ActorOf(Props.Create(() => new UpstreamEntityActor(_ =>
        {
            startCount++;
            return Props.Create<NullActor>();
        })));

        entity.Tell(new RegisterUpstream(target));
        entity.Tell(new RegisterUpstream(target));
        // Give actor time to process both messages
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, startCount);
    }

    // Minimal no-op actor used as stub for HealthCheckActor
    private sealed class NullActor : ReceiveActor { }

    // Forwards the first message it receives to a probe
    private sealed class ForwardingActor : ReceiveActor
    {
        public ForwardingActor(IActorRef probe)
        {
            ReceiveAny(msg => probe.Tell(msg));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.UpstreamEntityActorSpec"
```

Expected: compile failure — `UpstreamEntityActor` not found.

- [ ] **Step 3: Create `UpstreamEntityActor.cs`**

Create `src/Schleusenwerk/Routing/UpstreamEntityActor.cs`:

```csharp
using Akka.Actor;
using Akka.Event;

namespace Schleusenwerk.Routing;

public sealed class UpstreamEntityActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Func<UpstreamTarget, Props> _healthCheckPropsFactory;

    private UpstreamTarget? _target;
    private IActorRef? _healthCheckActor;

    public UpstreamEntityActor(Func<UpstreamTarget, Props> healthCheckPropsFactory)
    {
        _healthCheckPropsFactory = healthCheckPropsFactory;

        Receive<RegisterUpstream>(HandleRegisterUpstream);
        Receive<SelectUpstreamForDomain>(msg =>
        {
            if (_target is null)
            {
                Sender.Tell(new UpstreamNotFound(msg.Config.DomainName.Value));
                return;
            }
            Sender.Tell(new UpstreamResolved(_target, msg.Config));
        });
    }

    private void HandleRegisterUpstream(RegisterUpstream msg)
    {
        _target = msg.Target;

        if (_healthCheckActor == null)
        {
            _healthCheckActor = Context.ActorOf(
                _healthCheckPropsFactory(_target),
                "health-check");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.UpstreamEntityActorSpec"
```

Expected: all 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/Routing/UpstreamEntityActor.cs src/Schleusenwerk.Tests/Routing/UpstreamEntityActorSpec.cs
git commit -m "feat(routing): add UpstreamEntityActor shard entity with tests"
```

---

## Task 3: DomainEntityActor

**Files:**
- Create: `src/Schleusenwerk/Routing/DomainEntityActor.cs`
- Create: `src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs`
- Create: `src/Schleusenwerk.Tests/Routing/DomainEntityHealthSpec.cs`

- [ ] **Step 1: Write the failing routing tests**

Create `src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs`:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainEntityActorSpec : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;

    public DomainEntityActorSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private (IActorRef entity, TestProbe upstreamRegion) CreateEntity()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var upstreamRegion = CreateTestProbe();
        var entity = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamRegion)),
            $"entity-{Guid.NewGuid():N}");
        return (entity, upstreamRegion);
    }

    private static SetRoute MakeRoute(string domain, params string[] upstreams)
    {
        var config = new DomainConfig { DomainName = DomainName.Parse(domain) };
        var targets = upstreams.Select(UpstreamTarget.Create).ToList();
        return new SetRoute(config, targets);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_stash_ResolveUpstream_until_SetRoute_arrives()
    {
        var (entity, upstreamRegion) = CreateEntity();

        entity.Tell(new ResolveUpstream("example.com"));
        entity.Tell(MakeRoute("example.com", "http://a:8080"));

        // After SetRoute, the stashed ResolveUpstream is processed
        var forwarded = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Equal("example.com", forwarded.Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_forward_SelectUpstreamForDomain_to_upstream_region()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));

        entity.Tell(new ResolveUpstream("example.com"));

        var forwarded = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Equal("example.com", forwarded.Config.DomainName.Value);
        Assert.Contains("a", forwarded.Url);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_send_RegisterUpstream_per_upstream_on_SetRoute()
    {
        var (entity, upstreamRegion) = CreateEntity();

        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));

        var msgs = new List<RegisterUpstream>
        {
            upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout),
            upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout)
        };
        var urls = msgs.Select(m => m.Target.Url.Host).ToHashSet();
        Assert.Contains("a", urls);
        Assert.Contains("b", urls);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_round_robin_across_upstreams()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090", "http://c:7070"));
        // Drain RegisterUpstream messages
        upstreamRegion.ReceiveN(3, Timeout);

        var hosts = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            hosts.Add(new Uri(fwd.Url).Host);
        }

        Assert.Equal(2, hosts.Count(h => h == "a"));
        Assert.Equal(2, hosts.Count(h => h == "b"));
        Assert.Equal(2, hosts.Count(h => h == "c"));
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_reply_UpstreamNotFound_when_no_upstreams_in_route()
    {
        var (entity, _) = CreateEntity();
        entity.Tell(MakeRoute("example.com")); // empty upstream list

        var result = await entity.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);

        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_publish_RoutesUpdated_on_SetRoute_when_Ready()
    {
        var tcs = new TaskCompletionSource<RoutesUpdated>();
        var subscriber = Sys.ActorOf(Props.Create(() => new TcsActor<RoutesUpdated>(tcs)));
        Sys.EventStream.Subscribe(subscriber, typeof(RoutesUpdated));

        var (entity, _) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080")); // first SetRoute triggers Become(Ready)
        entity.Tell(MakeRoute("example.com", "http://b:9090")); // second SetRoute in Ready publishes event

        var evt = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Single(evt.Domains);
        Assert.Equal("example.com", evt.Domains[0].Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_publish_RouteRemoved_on_RemoveDomain()
    {
        var tcs = new TaskCompletionSource<RouteRemoved>();
        var subscriber = Sys.ActorOf(Props.Create(() => new TcsActor<RouteRemoved>(tcs)));
        Sys.EventStream.Subscribe(subscriber, typeof(RouteRemoved));

        var (entity, _) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));
        entity.Tell(new RemoveDomain(DomainName.Parse("example.com")));

        var evt = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("example.com", evt.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_update_config_on_second_SetRoute()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://old:8080"));
        upstreamRegion.ReceiveN(1, Timeout);

        entity.Tell(MakeRoute("example.com", "http://new:9090"));
        upstreamRegion.ExpectMsg<RegisterUpstream>(Timeout);

        entity.Tell(new ResolveUpstream("example.com"));
        var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Contains("new", fwd.Url);
    }

    private sealed class TcsActor<T> : ReceiveActor
    {
        public TcsActor(TaskCompletionSource<T> tcs)
        {
            Receive<T>(tcs.TrySetResult);
        }
    }
}
```

- [ ] **Step 2: Write the failing health tests**

Create `src/Schleusenwerk.Tests/Routing/DomainEntityHealthSpec.cs`:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class DomainEntityHealthSpec : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;
    private IActorRef _hub = null!;

    public DomainEntityHealthSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private (IActorRef entity, TestProbe upstreamRegion) CreateEntity()
    {
        _hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(_hub, overwrite: true);

        var upstreamRegion = CreateTestProbe();
        var entity = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamRegion)),
            $"entity-{Guid.NewGuid():N}");
        return (entity, upstreamRegion);
    }

    private static SetRoute MakeRoute(string domain, params string[] upstreams)
    {
        var config = new DomainConfig { DomainName = DomainName.Parse(domain) };
        var targets = upstreams.Select(UpstreamTarget.Create).ToList();
        return new SetRoute(config, targets);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_exclude_unhealthy_upstream_received_via_EventHub()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));
        upstreamRegion.ReceiveN(2, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        for (var i = 0; i < 3; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            Assert.Contains("b", fwd.Url);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_return_UpstreamNotFound_when_all_unhealthy()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));
        upstreamRegion.ReceiveN(1, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        var result = await entity.Ask<UpstreamNotFound>(new ResolveUpstream("example.com"), Timeout);
        Assert.Equal("example.com", result.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_restore_upstream_after_healthy_event()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));
        upstreamRegion.ReceiveN(2, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);
        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: true));
        await Task.Delay(150);

        var hosts = new HashSet<string>();
        for (var i = 0; i < 6; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            hosts.Add(new Uri(fwd.Url).Host);
        }

        Assert.Contains("a", hosts);
        Assert.Contains("b", hosts);
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_ignore_duplicate_unhealthy_events()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080", "http://b:9090"));
        upstreamRegion.ReceiveN(2, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://a:8080"), IsHealthy: false));
        await Task.Delay(150);

        for (var i = 0; i < 3; i++)
        {
            entity.Tell(new ResolveUpstream("example.com"));
            var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
            Assert.Contains("b", fwd.Url);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task DomainEntityActor_should_ignore_healthy_event_for_unknown_upstream()
    {
        var (entity, upstreamRegion) = CreateEntity();
        entity.Tell(MakeRoute("example.com", "http://a:8080"));
        upstreamRegion.ReceiveN(1, Timeout);

        _hub.Tell(new UpstreamHealthChanged(UpstreamUrl.Parse("http://unknown:9999"), IsHealthy: true));
        await Task.Delay(100);

        entity.Tell(new ResolveUpstream("example.com"));
        var fwd = upstreamRegion.ExpectMsg<SelectUpstreamForDomain>(Timeout);
        Assert.Contains("a", fwd.Url);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -namespace "Schleusenwerk.Tests.Routing"
```

Expected: compile failure — `DomainEntityActor` not found.

- [ ] **Step 4: Create `DomainEntityActor.cs`**

Create `src/Schleusenwerk/Routing/DomainEntityActor.cs`:

```csharp
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Routing;

public sealed class DomainEntityActor : ReceiveActor, IWithUnboundedStash
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _upstreamRegion;
    private readonly IActorRef _eventHub;

    private DomainConfig _config = null!;
    private List<UpstreamUrl> _upstreams = [];
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private int _roundRobinIndex;

    public IStash Stash { get; set; } = null!;

    public DomainEntityActor(IActorRef upstreamRegion)
    {
        _upstreamRegion = upstreamRegion;
        _eventHub = Context.GetActor<EventHub>();
        WaitingForSubscription();
    }

    protected override void PreStart()
    {
        _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance)
            .PipeTo(Self);
    }

    private void WaitingForSubscription()
    {
        Receive<EventHub.Subscribed>(msg =>
        {
            var self = Self;
            msg.SourceRef.Source.RunForeach(evt => self.Tell(evt), Context.Materializer());
            Become(WaitingForRoute);
        });
        Receive<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "DomainEntityActor failed to subscribe to EventHub — retrying");
            _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance).PipeTo(Self);
        });
        ReceiveAny(_ => Stash.Stash());
    }

    private void WaitingForRoute()
    {
        Receive<SetRoute>(msg =>
        {
            ApplySetRoute(msg);
            Stash.UnstashAll();
            Become(Ready);
        });
        Receive<ResolveUpstream>(_ => Stash.Stash());
        ReceiveAny(_ => { });
    }

    private void Ready()
    {
        Receive<SetRoute>(msg =>
        {
            ApplySetRoute(msg);
            Context.System.EventStream.Publish(new RoutesUpdated([_config.DomainName]));
        });
        Receive<ResolveUpstream>(HandleResolveUpstream);
        Receive<RemoveDomain>(_ =>
        {
            Context.System.EventStream.Publish(new RouteRemoved(_config.DomainName));
            Self.Tell(PoisonPill.Instance);
        });
        Receive<UpstreamHealthChanged>(msg =>
        {
            if (msg.IsHealthy)
                _unhealthyUrls.Remove(msg.Url);
            else
                _unhealthyUrls.Add(msg.Url);
        });
        Receive<IClusterEvent>(_ => { });
    }

    private void ApplySetRoute(SetRoute msg)
    {
        _config = msg.Config;
        _upstreams = msg.Upstreams.Select(u => u.Url).ToList();
        _unhealthyUrls.IntersectWith(_upstreams.ToHashSet());
        _roundRobinIndex = 0;
        foreach (var upstream in msg.Upstreams)
        {
            _upstreamRegion.Tell(new RegisterUpstream(upstream));
        }
    }

    private void HandleResolveUpstream(ResolveUpstream msg)
    {
        var healthy = _upstreams.Where(u => !_unhealthyUrls.Contains(u)).ToList();
        if (healthy.Count == 0)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }
        var picked = healthy[_roundRobinIndex % healthy.Count];
        _roundRobinIndex++;
        _upstreamRegion.Tell(new SelectUpstreamForDomain(_config, picked.Value.ToString()), Sender);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -namespace "Schleusenwerk.Tests.Routing"
```

Expected: `DomainEntityActorSpec` (8 tests) and `DomainEntityHealthSpec` (5 tests) PASS. `DomainRouterActorSpec` and `DomainRouterHealthSpec` will fail to compile until Task 7.

> **Note:** If the existing `DomainRouterActorSpec` / `DomainRouterHealthSpec` prevent compilation, temporarily comment them out or add `#if false` guards. They will be deleted in Task 7.

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk/Routing/DomainEntityActor.cs \
        src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs \
        src/Schleusenwerk.Tests/Routing/DomainEntityHealthSpec.cs
git commit -m "feat(routing): add DomainEntityActor shard entity with tests"
```

---

## Task 4: Cluster Wiring + Shard Regions

**Files:**
- Modify: `src/Directory.Packages.props`
- Modify: `src/Schleusenwerk/Schleusenwerk.csproj`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs`

- [ ] **Step 1: Add `Akka.Cluster.Hosting` to `Directory.Packages.props`**

In `src/Directory.Packages.props`, add to the Akka.NET ItemGroup:

```xml
<PackageVersion Include="Akka.Cluster.Hosting" Version="1.5.65"/>
```

- [ ] **Step 2: Add package reference to `Schleusenwerk.csproj`**

In `src/Schleusenwerk/Schleusenwerk.csproj`, add to the first ItemGroup:

```xml
<PackageReference Include="Akka.Cluster.Hosting" />
```

- [ ] **Step 3: Rewrite `SchleusenwerkActorSystemSetup.cs`**

Replace the full file with:

```csharp
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Persistence.Sql.Hosting;
using Akka.Remote.Hosting;
using LinqToDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Discovery;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkActorSystemSetup : ActorSystemSetupContainer
{
    protected override string GetActorSystemName() => "schleusenwerk";

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

        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        Func<UpstreamTarget, Props> healthCheckPropsFactory = upstream =>
        {
            var config = new HealthCheckConfig();
            Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> probeFunc =
                async (url, endpoint, timeout, ct) =>
                {
                    using var client = httpClientFactory.CreateClient("health-check");
                    client.Timeout = timeout;
                    try
                    {
                        var uri = new Uri($"{url}{endpoint.TrimStart('/')}");
                        using var response = await client.GetAsync(uri, ct);
                        return response.IsSuccessStatusCode;
                    }
                    catch
                    {
                        return false;
                    }
                };

            return Props.Create(() => new HealthCheckActor(upstream.Url, config, probeFunc));
        };

        // Register upstream region first — DomainEntityActor props factory reads it from registry
        builder.WithShardRegion<UpstreamEntityActor>(
            "upstream-pool",
            (_, _, _) => _ => Props.Create(() => new UpstreamEntityActor(healthCheckPropsFactory)),
            new MessageExtractor<IWithUrl>(
                maxNumberOfShards: 20,
                idExtractor: m => m.Url,
                shardIdExtractor: m => Math.Abs(m.Url.GetHashCode() % 20).ToString()),
            new ShardOptions { PassivateIdleEntityAfter = TimeSpan.FromMinutes(5) });

        builder.WithShardRegion<DomainEntityActor>(
            "domain-router",
            (_, registry, _) => _ => Props.Create(() => new DomainEntityActor(registry.Get<UpstreamEntityActor>())),
            new MessageExtractor<IWithDomain>(
                maxNumberOfShards: 20,
                idExtractor: m => m.Domain,
                shardIdExtractor: m => Math.Abs(m.Domain.GetHashCode() % 20).ToString()),
            new ShardOptions { PassivateIdleEntityAfter = TimeSpan.FromMinutes(5) });

        builder.WithActors((system, registry, resolver) =>
        {
            var eventHub = system.ActorOf(resolver.Props<EventHub>(), "eventHub");
            registry.Register<EventHub>(eventHub);

            var config = system.ActorOf(resolver.Props<ConfigurationPersistenceActor>(), "configuration");
            registry.Register<ConfigurationPersistenceActor>(config);

            var dockerDiscovery = system.ActorOf(resolver.Props<DockerDiscoveryActor>(), "docker-discovery");
            registry.Register<DockerDiscoveryActor>(dockerDiscovery);
        });
    }
}
```

> **Note on `MessageExtractor<T>`:** The exact Akka.Cluster.Hosting API for `WithShardRegion` varies slightly by version. If `MessageExtractor<T>` doesn't exist, use the functional delegate form:
> ```csharp
> extractEntityId: msg => msg is IWithDomain m
>     ? Option<(string, object)>.Create((m.Domain, msg))
>     : Option<(string, object)>.None,
> extractShardId: msg => msg is IWithDomain m
>     ? Math.Abs(m.Domain.GetHashCode() % 20).ToString()
>     : null,
> ```
> Check the available overloads in `Akka.Cluster.Hosting.AkkaClusterHostingExtensions` and adjust accordingly.

- [ ] **Step 4: Run dotnet restore**

```bash
dotnet restore src/Schleusenwerk.slnx
```

Expected: packages resolve successfully.

- [ ] **Step 5: Build (partial — some compilation errors from deleted files expected)**

```bash
dotnet build --configuration Release src/Schleusenwerk.slnx
```

Expected: errors only from files that still reference `DomainRouterActor`/`LoadBalancerActor`/`UpdateRoutes` (i.e., `ConfigurationPersistenceActor.cs`, `ProxyDispatcher.cs`, the old test files). The main `Schleusenwerk` project (excluding `SchleusenwerkActorSystemSetup`) should compile cleanly.

- [ ] **Step 6: Commit**

```bash
git add src/Directory.Packages.props src/Schleusenwerk/Schleusenwerk.csproj src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs
git commit -m "feat(startup): wire DomainEntityActor and UpstreamEntityActor shard regions"
```

---

## Task 5: ConfigurationPersistenceActor — SetRoute Integration

**Files:**
- Modify: `src/Schleusenwerk/Persistence/ConfigurationPersistenceActor.cs`

`ConfigurationPersistenceActor` must send `SetRoute` to `DomainEntityActor` shard region after recovery completes (via `WaitingForPublisher` → `Ready` transition) and after each relevant state mutation (domain updated, upstream added/removed). It must send routing `RemoveDomain` when a domain is removed.

- [ ] **Step 1: Modify `ConfigurationPersistenceActor.cs`**

Add `_domainRegion` field and update the three areas: constructor, `WaitingForPublisher` transition, and the persist callbacks.

Add to `using` directives:
```csharp
using RoutingRemoveDomain = Schleusenwerk.Routing.RemoveDomain;
```

Add field after `_publishQueue`:
```csharp
private IActorRef _domainRegion = null!;
```

At the end of the constructor, before `WaitingForPublisher()`:
```csharp
// _domainRegion is resolved lazily after actor system starts
```

Add `_domainRegion` initialisation inside the `WaitingForPublisher` `Command<EventHub.PublisherReady>` handler, just before `Become(Ready)`:
```csharp
_domainRegion = Context.GetActor<DomainEntityActor>();

// Replay all known routes into the shard region after recovery
foreach (var (domainName, domainConfig) in _state.Domains)
{
    var upstreams = _state.Upstreams.TryGetValue(domainName, out var list)
        ? list
        : (IReadOnlyList<UpstreamTarget>)[];
    _domainRegion.Tell(new SetRoute(domainConfig, upstreams));
}
```

Update the persist callbacks:

In `Handle(UpdateDomain cmd)`, after `PublishClusterEvent(evt)`:
```csharp
var upstreams = _state.Upstreams.TryGetValue(cmd.Config.DomainName, out var list)
    ? list
    : (IReadOnlyList<UpstreamTarget>)[];
_domainRegion.Tell(new SetRoute(_state.Domains[cmd.Config.DomainName], upstreams));
```

In `Handle(AddUpstream cmd)`, after `Sender.Tell(ConfigurationCommandAck.Instance)`:
```csharp
var upstreams = _state.Upstreams[cmd.DomainName];
_domainRegion.Tell(new SetRoute(_state.Domains[cmd.DomainName], upstreams));
```

In `Handle(RemoveUpstream cmd)`, after `Sender.Tell(ConfigurationCommandAck.Instance)`:
```csharp
var upstreams = _state.Upstreams.TryGetValue(cmd.DomainName, out var list)
    ? list
    : (IReadOnlyList<UpstreamTarget>)[];
_domainRegion.Tell(new SetRoute(_state.Domains[cmd.DomainName], upstreams));
```

In `Handle(RemoveDomain cmd)`, after `PublishClusterEvent(evt)`:
```csharp
_domainRegion.Tell(new RoutingRemoveDomain(cmd.DomainName));
```

The full updated `ConfigurationPersistenceActor.cs`:

```csharp
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Routing;
using Servus.Akka;
using RoutingRemoveDomain = Schleusenwerk.Routing.RemoveDomain;

namespace Schleusenwerk.Persistence;

public sealed class ConfigurationPersistenceActor : ReceivePersistentActor, IWithUnboundedStash
{
    public override string PersistenceId => "configuration";
    public new IStash Stash { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ConfigurationState _state = new();
    private readonly int _snapshotInterval;
    private readonly int _keepSnapshots;
    private readonly IActorRef _eventHub;
    private IMaterializer _materializer = null!;
    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;
    private IActorRef _domainRegion = null!;

    public ConfigurationPersistenceActor(int snapshotInterval = 100, int keepSnapshots = 3)
    {
        _eventHub = Context.GetActor<EventHub>();
        _snapshotInterval = snapshotInterval;
        _keepSnapshots = keepSnapshots;

        Recover<DomainAdded>(evt => _state.Apply(evt));
        Recover<DomainUpdated>(evt => _state.Apply(evt));
        Recover<DomainRemoved>(evt => _state.Apply(evt));
        Recover<UpstreamAdded>(evt => _state.Apply(evt));
        Recover<UpstreamRemoved>(evt => _state.Apply(evt));
        Recover<SettingsUpdated>(evt => _state.Apply(evt));
        Recover<SnapshotOffer>(offer =>
        {
            if (offer.Snapshot is ConfigurationSnapshot snapshot)
            {
                _state.RestoreFromSnapshot(snapshot);
                _log.Info("Restored configuration from snapshot at sequence {SequenceNr}", offer.Metadata.SequenceNr);
            }
        });

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

            _domainRegion = Context.GetActor<DomainEntityActor>();

            foreach (var (domainName, domainConfig) in _state.Domains)
            {
                var upstreams = _state.Upstreams.TryGetValue(domainName, out var list)
                    ? list
                    : (IReadOnlyList<UpstreamTarget>)[];
                _domainRegion.Tell(new SetRoute(domainConfig, upstreams));
            }

            _log.Info("Publisher channel to EventHubActor established; replayed {Count} route(s)", _state.Domains.Count);
            Become(Ready);
            Stash.UnstashAll();
        });
        Command<Status.Failure>(f =>
        {
            _log.Error(f.Cause, "Failed to get publisher from EventHubActor — retrying");
            _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
                .PipeTo(Self);
        });
        CommandAny(_ => Stash.Stash());
    }

    private void Ready()
    {
        Command<AddDomain>(Handle);
        Command<UpdateDomain>(Handle);
        Command<RemoveDomain>(Handle);
        Command<AddUpstream>(Handle);
        Command<RemoveUpstream>(Handle);
        Command<UpdateSettings>(Handle);
        Command<GetConfiguration>(Handle);
        Command<GetDomainByName>(Handle);
        Command<GetSettings>(Handle);
        Command<SaveSnapshotSuccess>(msg =>
        {
            var upperBound = new SnapshotSelectionCriteria(
                msg.Metadata.SequenceNr - _keepSnapshots * _snapshotInterval);
            DeleteSnapshots(upperBound);
        });
        Command<SaveSnapshotFailure>(msg =>
            _log.Error(msg.Cause, "Failed to save snapshot at sequence {SequenceNr}", msg.Metadata.SequenceNr));
        Command<DeleteSnapshotsSuccess>(_ => { });
        Command<DeleteSnapshotsFailure>(msg =>
            _log.Warning(msg.Cause, "Failed to delete old snapshots"));
    }

    private void Handle(AddDomain cmd)
    {
        var validation = ConfigurationValidator.ValidateAddDomain(cmd.Config, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new DomainAdded(cmd.Config);
        PersistAndApply(evt, () =>
        {
            _log.Info("Domain added: {Domain}", cmd.Config.DomainName);
            PublishClusterEvent(evt);
            PublishClusterEvent(new CertificateProvisioningRequested(cmd.Config.DomainName));
            // No SetRoute here — no upstreams yet; DomainEntityActor will stash ResolveUpstream
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(UpdateDomain cmd)
    {
        var validation = ConfigurationValidator.ValidateUpdateDomain(cmd.Config, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new DomainUpdated(cmd.Config);
        PersistAndApply(evt, () =>
        {
            _log.Info("Domain updated: {Domain}", cmd.Config.DomainName);
            PublishClusterEvent(evt);
            var upstreams = _state.Upstreams.TryGetValue(cmd.Config.DomainName, out var list)
                ? list
                : (IReadOnlyList<UpstreamTarget>)[];
            _domainRegion.Tell(new SetRoute(_state.Domains[cmd.Config.DomainName], upstreams));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(RemoveDomain cmd)
    {
        var validation = ConfigurationValidator.ValidateRemoveDomain(cmd.DomainName, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new DomainRemoved(cmd.DomainName);
        PersistAndApply(evt, () =>
        {
            _log.Info("Domain removed: {Domain}", cmd.DomainName);
            PublishClusterEvent(evt);
            _domainRegion.Tell(new RoutingRemoveDomain(cmd.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(AddUpstream cmd)
    {
        var validation = ConfigurationValidator.ValidateAddUpstream(cmd.DomainName, cmd.Upstream, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new UpstreamAdded(cmd.DomainName, cmd.Upstream);
        PersistAndApply(evt, () =>
        {
            _log.Info("Upstream added to {Domain}: {Url}", cmd.DomainName, cmd.Upstream.Url);
            var upstreams = _state.Upstreams[cmd.DomainName];
            _domainRegion.Tell(new SetRoute(_state.Domains[cmd.DomainName], upstreams));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(RemoveUpstream cmd)
    {
        var validation = ConfigurationValidator.ValidateRemoveUpstream(cmd.DomainName, cmd.UpstreamUrl, _state);
        if (validation is ConfigurationResult.Failure failure)
        {
            Sender.Tell(new ConfigurationCommandNack(failure.Error));
            return;
        }

        var evt = new UpstreamRemoved(cmd.DomainName, cmd.UpstreamUrl);
        PersistAndApply(evt, () =>
        {
            _log.Info("Upstream removed from {Domain}: {Url}", cmd.DomainName, cmd.UpstreamUrl);
            var upstreams = _state.Upstreams.TryGetValue(cmd.DomainName, out var list)
                ? list
                : (IReadOnlyList<UpstreamTarget>)[];
            _domainRegion.Tell(new SetRoute(_state.Domains[cmd.DomainName], upstreams));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(UpdateSettings cmd)
    {
        var evt = new SettingsUpdated(cmd.Settings);
        PersistAndApply(evt, () =>
        {
            _log.Info("Proxy settings updated");
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void Handle(GetConfiguration _)
    {
        Sender.Tell(_state.ToSnapshot());
    }

    private void Handle(GetDomainByName query)
    {
        if (!_state.HasDomain(query.DomainName))
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{query.DomainName}' does not exist."));
            return;
        }

        var config = _state.Domains[query.DomainName];
        var upstreams = _state.Upstreams.TryGetValue(query.DomainName, out var list)
            ? list
            : (IReadOnlyList<UpstreamTarget>)[];

        Sender.Tell(new DomainConfigResult(config, upstreams));
    }

    private void Handle(GetSettings _)
    {
        Sender.Tell(_state.Settings);
    }

    private void PublishClusterEvent(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Done.Instance,
            failure: ex => new PublishFailed(ex));
    }

    private void PersistAndApply<TEvent>(TEvent evt, Action afterPersist) where TEvent : notnull
    {
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            afterPersist();
            SaveSnapshotIfNeeded();
        });
    }

    private void ApplyEvent(object evt)
    {
        switch (evt)
        {
            case DomainAdded e: _state.Apply(e); break;
            case DomainUpdated e: _state.Apply(e); break;
            case DomainRemoved e: _state.Apply(e); break;
            case UpstreamAdded e: _state.Apply(e); break;
            case UpstreamRemoved e: _state.Apply(e); break;
            case SettingsUpdated e: _state.Apply(e); break;
        }
    }

    private void SaveSnapshotIfNeeded()
    {
        if (_snapshotInterval > 0 && _state.EventCount % _snapshotInterval == 0)
        {
            SaveSnapshot(_state.ToSnapshot());
            _log.Info("Snapshot saved at event count {Count}", _state.EventCount);
        }
    }

    private sealed record PublishDropped(IClusterEvent Event);
    private sealed record PublishFailed(Exception Exception);
}
```

- [ ] **Step 2: Verify the project compiles (excluding old test files)**

```bash
dotnet build --configuration Release src/Schleusenwerk/Schleusenwerk.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Persistence/ConfigurationPersistenceActor.cs
git commit -m "feat(persistence): send SetRoute to domain shard region on recovery and mutations"
```

---

## Task 6: ProxyDispatcher Migration

**Files:**
- Modify: `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs`
- Modify: `src/Schleusenwerk.Tests/Forwarding/ProxyDispatcherSpec.cs`

- [ ] **Step 1: Update `ProxyDispatcher.cs`**

Replace the full file:

```csharp
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
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);

    public ProxyDispatcher(
        IRequiredActor<DomainEntityActor> domainRegionProvider,
        RequestForwardingPipeline pipeline,
        HeaderManipulationFilter headerFilter,
        WebSocketTunnel webSocketTunnel)
    {
        _domainRegion = domainRegionProvider.ActorRef;
        _pipeline = pipeline;
        _headerFilter = headerFilter;
        _webSocketTunnel = webSocketTunnel;
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
            AskTimeout,
            ct);

        switch (response)
        {
            case UpstreamResolved resolved:
                await HandleResolvedRoute(context, resolved.Target, resolved.Config, ct);
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

- [ ] **Step 2: Update `ProxyDispatcherSpec.cs`**

Replace all occurrences in the spec:
- Remove `using Schleusenwerk.LoadBalancing;`
- Change `DomainRouterActor` → `DomainEntityActor`
- Change `UpdateRoutes([route])` → `SetRoute(config, targets)` form
- Remove `router.Ask<UpstreamResolved>` warm-up calls (new entities activate on first message, no pre-warming needed)

Replace the full file:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http;
using Schleusenwerk.Forwarding;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using TurboHTTP;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class ProxyDispatcherSpec : TestKit
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;

    public ProxyDispatcherSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private (IActorRef domainRegion, IActorRef upstreamRegion) CreateRegions()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var upstreamRegion = CreateTestProbe();
        var domainRegion = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamRegion)),
            $"domain-{Guid.NewGuid():N}");
        _registry.Register<DomainEntityActor>(domainRegion, overwrite: true);

        return (domainRegion, upstreamRegion);
    }

    private static SetRoute MakeRoute(
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
        var targets = upstreams.Select(UpstreamTarget.Create).ToList();
        return new SetRoute(config, targets);
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

    // Simulates UpstreamEntityActor responding directly to the ProxyDispatcher's Ask PromiseRef
    private IActorRef CreateUpstreamRegionThatReplies(UpstreamTarget target, DomainConfig config)
    {
        return Sys.ActorOf(Props.Create(() => new ReplyingUpstreamActor(target, config)));
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
    public async Task Dispatcher_should_return_404_when_domain_not_configured()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);
        var upstreamProbe = CreateTestProbe();
        var domainRegion = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamProbe)),
            $"domain-{Guid.NewGuid():N}");
        _registry.Register<DomainEntityActor>(domainRegion, overwrite: true);

        var dispatcher = CreateDispatcher(domainRegion);
        var context = CreateHttpContext("unknown.example.com", "/test");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_forward_request_when_domain_is_configured()
    {
        var target = UpstreamTarget.Create("http://backend:8080");
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var upstreamRegion = CreateUpstreamRegionThatReplies(target, config);
        var domainRegion = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamRegion)),
            $"domain-{Guid.NewGuid():N}");
        _registry.Register<DomainEntityActor>(domainRegion, overwrite: true);

        domainRegion.Tell(new SetRoute(config, [target]));
        await Task.Delay(100); // Allow actor to become Ready

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

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var upstreamRegion = CreateUpstreamRegionThatReplies(target, config);
        var domainRegion = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamRegion)),
            $"domain-{Guid.NewGuid():N}");
        _registry.Register<DomainEntityActor>(domainRegion, overwrite: true);

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
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);
        var upstreamProbe = CreateTestProbe();
        var domainRegion = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(upstreamProbe)),
            $"domain-{Guid.NewGuid():N}");
        _registry.Register<DomainEntityActor>(domainRegion, overwrite: true);

        var dispatcher = CreateDispatcher(domainRegion);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(string.Empty);
        context.Request.Path = "/test";
        context.Request.Method = "GET";

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    // Simulates UpstreamEntityActor: responds to SelectUpstreamForDomain with UpstreamResolved
    private sealed class ReplyingUpstreamActor : ReceiveActor
    {
        public ReplyingUpstreamActor(UpstreamTarget target, DomainConfig config)
        {
            Receive<SelectUpstreamForDomain>(_ => Sender.Tell(new UpstreamResolved(target, config)));
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
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
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
```

- [ ] **Step 3: Run the ProxyDispatcher tests**

```bash
dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Forwarding.ProxyDispatcherSpec"
```

Expected: all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk/Forwarding/ProxyDispatcher.cs src/Schleusenwerk.Tests/Forwarding/ProxyDispatcherSpec.cs
git commit -m "feat(forwarding): switch ProxyDispatcher to DomainEntityActor shard region"
```

---

## Task 7: Delete Old Code + Final Build Verification

**Files:**
- Delete: `src/Schleusenwerk/Routing/DomainRouterActor.cs`
- Delete: `src/Schleusenwerk/LoadBalancing/LoadBalancerActor.cs`
- Delete: `src/Schleusenwerk/LoadBalancing/UpstreamRouteeActor.cs`
- Delete: `src/Schleusenwerk/LoadBalancing/Messages.cs`
- Delete: `src/Schleusenwerk.Tests/Routing/DomainRouterActorSpec.cs`
- Delete: `src/Schleusenwerk.Tests/Routing/DomainRouterHealthSpec.cs`
- Delete: `src/Schleusenwerk.Tests/LoadBalancing/LoadBalancerActorSpec.cs`

- [ ] **Step 1: Delete old source files**

```bash
Remove-Item src/Schleusenwerk/Routing/DomainRouterActor.cs
Remove-Item src/Schleusenwerk/LoadBalancing/LoadBalancerActor.cs
Remove-Item src/Schleusenwerk/LoadBalancing/UpstreamRouteeActor.cs
Remove-Item src/Schleusenwerk/LoadBalancing/Messages.cs
```

- [ ] **Step 2: Delete old test files**

```bash
Remove-Item src/Schleusenwerk.Tests/Routing/DomainRouterActorSpec.cs
Remove-Item src/Schleusenwerk.Tests/Routing/DomainRouterHealthSpec.cs
Remove-Item src/Schleusenwerk.Tests/LoadBalancing/LoadBalancerActorSpec.cs
```

- [ ] **Step 3: Full build — must be clean**

```bash
dotnet build --configuration Release src/Schleusenwerk.slnx
```

Expected: **0 errors, 0 warnings** (or only pre-existing unrelated warnings).

- [ ] **Step 4: Run full test suite**

```bash
dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj
```

Expected: all tests PASS. Verify that `DomainEntityActorSpec`, `DomainEntityHealthSpec`, `UpstreamEntityActorSpec`, and `ProxyDispatcherSpec` are in the results.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(routing): remove DomainRouterActor, LoadBalancerActor and related load-balancing code"
```

---

## Self-Review Notes

| Spec requirement | Covered in |
|---|---|
| `IWithDomain`/`IWithUrl` string interfaces for entity-ID extraction | Task 1 |
| `SetRoute`, `RegisterUpstream`, `SelectUpstreamForDomain` messages | Task 1 |
| `DomainEntityActor` three-state behavior (WaitingForSubscription→WaitingForRoute→Ready) | Task 3 |
| Round-robin across healthy upstreams | Task 3 (`DomainEntityActorSpec` round-robin test) |
| `UpstreamEntityActor` single Ready behavior, owns HealthCheckActor | Task 2 |
| Forward pattern preserving original Sender (no Ask) | Task 3 `DomainEntityActor`, Task 6 `ReplyingUpstreamActor` test |
| `UpstreamHealthChanged` updates `_unhealthyUrls` in `DomainEntityActor` | Task 3 (`DomainEntityHealthSpec`) |
| `RemoveDomain` → `RouteRemoved` + `PoisonPill` | Task 3 |
| `RoutesUpdated` published on second+ `SetRoute` (Ready state) | Task 3 |
| `ConfigurationPersistenceActor` sends `SetRoute` on recovery + mutations | Task 5 |
| `ConfigurationPersistenceActor` sends routing `RemoveDomain` | Task 5 |
| Shard region wiring with `WithShardRegion<T>` | Task 4 |
| `ProxyDispatcher` uses `IRequiredActor<DomainEntityActor>` | Task 6 |
| Old `DomainRouterActor`/`LoadBalancerActor` deleted | Task 7 |
| 20 shards per region (10× max node count) | Task 4 (shard config) |
| `UpstreamEntityActor` idle passivation 5 min | Task 4 (ShardOptions) |
