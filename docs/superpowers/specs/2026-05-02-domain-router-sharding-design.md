# Domain Router — Cluster Sharding Redesign

**Date:** 2026-05-02  
**Status:** Approved  
**Goal:** Replace the single `DomainRouterActor` with two Akka.Cluster.Sharding regions to eliminate the routing bottleneck under high domain count.

---

## Motivation

The current `DomainRouterActor` is a single actor managing all domains. Under high domain counts it becomes a throughput bottleneck because every `ResolveUpstream` request serializes through one mailbox. Sharding distributes domain and upstream entities across cluster nodes so throughput scales horizontally.

---

## Architecture

Two `ShardRegion`s replace `DomainRouterActor` and `LoadBalancerActor`:

| ShardRegion | Entity-ID | One entity per |
|---|---|---|
| `ShardRegion<DomainEntityActor>` | Domainname (`"example.com"`) | Domain |
| `ShardRegion<UpstreamEntityActor>` | Upstream-URL (`"http://a:8080"`) | Upstream-URL |

### Request path

```
ProxyRequestHandler
  │  ResolveUpstream("example.com")   ← Sender = self
  ▼
DomainEntityActor("example.com")
  │  wählt nächsten gesunden Upstream per Round-Robin
  │  Tell(SelectUpstreamForDomain(config, url), Sender=ProxyRequestHandler)
  ▼
UpstreamEntityActor("http://a:8080")  ← Sender = ProxyRequestHandler
  │  UpstreamResolved(target, config)
  ▼
ProxyRequestHandler
```

Kein `Ask`, kein doppelter Roundtrip. `DomainEntityActor` reicht den originalen `Sender` weiter.

### Configuration path

```
ConfigurationPersistenceActor
  └─ SetRoute(config, upstreams) ──► DomainEntityActor
                                        └─ RegisterUpstream(target) ──► UpstreamEntityActor (pro Upstream)
```

### Health path

```
UpstreamEntityActor
  └─ HealthCheckActor (Kind)
       └─ publiziert UpstreamHealthChanged ──► EventHub
                                                  └─ StreamRef ──► DomainEntityActor
                                                                       └─ pflegt _unhealthyUrls
```

---

## Message Protocol

### Neue Interfaces

```csharp
interface IWithDomain { string Domain { get; } }
interface IWithUrl    { string Url { get; } }
```

Werden von allen Nachrichten implementiert, die an eine der beiden ShardRegions gehen. Ermöglicht triviale Entity-ID-Extraktion ohne Pattern-Matching.

### Neue Nachrichten (`Routing/`)

| Nachricht | Interface | Beschreibung |
|---|---|---|
| `SetRoute(DomainConfig, IReadOnlyList<UpstreamTarget>)` | `IWithDomain` | Ersetzt `UpdateRoutes` (Batch). Wird pro Domain gesendet. |
| `RegisterUpstream(UpstreamTarget)` | `IWithUrl` | `DomainEntityActor` → `UpstreamEntityActor` bei `SetRoute` |
| `SelectUpstreamForDomain(DomainConfig, string Url)` | `IWithUrl` | Forward von `DomainEntityActor` → `UpstreamEntityActor` mit originalem Sender |

### Bestehende Nachrichten die unverändert bleiben

`ResolveUpstream`, `UpstreamResolved`, `UpstreamNotFound`, `RemoveDomain`, `RoutesUpdated`, `RouteRemoved`

### Wegfallende Nachrichten

`UpdateRoutes` (Batch-Form), `SelectUpstream` (Singleton), `MarkUpstreamHealthy`, `MarkUpstreamUnhealthy`, `UpdateUpstreams`

### Interface-Implementierungen

```csharp
record SetRoute(DomainConfig Config, IReadOnlyList<UpstreamTarget> Upstreams) : IWithDomain
    { public string Domain => Config.DomainName.Value; }

record ResolveUpstream(string Host) : IWithDomain
    { public string Domain => Host.ToLowerInvariant(); }

record RemoveDomain(DomainName DomainName) : IWithDomain
    { public string Domain => DomainName.Value; }

record RegisterUpstream(UpstreamTarget Target) : IWithUrl
    { public string Url => Target.Url.Value.ToString(); }

record SelectUpstreamForDomain(DomainConfig Config, string Url) : IWithUrl;
```

### Entity-ID-Extraktion

```csharp
// DomainEntityActor
ExtractEntityId = msg => msg is IWithDomain m ? (m.Domain, msg) : (null, null);

// UpstreamEntityActor
ExtractEntityId = msg => msg is IWithUrl m ? (m.Url, msg) : (null, null);
```

---

## DomainEntityActor

**Datei:** `Routing/DomainEntityActor.cs`

**State:**
- `DomainConfig _config`
- `List<UpstreamUrl> _upstreams` — geordnet für stabiles Round-Robin
- `HashSet<UpstreamUrl> _unhealthyUrls`
- `int _roundRobinIndex`
- `IActorRef _upstreamRegion` (per Konstruktor injiziert)
- `IStash Stash`

**Behavior:**

```
PreStart:
  EventHub.Ask<Subscribed>(Subscribe).PipeTo(Self)
  Become(WaitingForSubscription)

WaitingForSubscription:  [stasht alles außer Subscribed/Failure]
  Subscribed  → StreamRef.RunForeach(evt => Self.Tell(evt))
              → Become(WaitingForRoute)
  Failure     → Retry

WaitingForRoute:  [stasht ResolveUpstream]
  SetRoute    → State speichern
              → RegisterUpstream pro Upstream an _upstreamRegion
              → Stash.UnstashAll()
              → Become(Ready)

Ready:
  SetRoute              → _config, _upstreams aktualisieren
                          → RegisterUpstream für neue Upstreams
                          → EventStream.Publish(new RoutesUpdated([domainName]))
  ResolveUpstream       → nächsten gesunden Upstream wählen (Round-Robin)
                          → falls gefunden: _upstreamRegion.Tell(
                              ShardEnvelope(url, SelectUpstreamForDomain(config, url)), Sender)
                          → falls alle unhealthy: Sender.Tell(new UpstreamNotFound(host))
  RemoveDomain          → EventStream.Publish(new RouteRemoved(domainName))
                          → Self.Tell(PoisonPill.Instance)
  UpstreamHealthChanged → _unhealthyUrls.Add/Remove je nach IsHealthy
```

---

## UpstreamEntityActor

**Datei:** `Routing/UpstreamEntityActor.cs`

`UpstreamEntityActor` braucht kein EventHub-Abo — health tracking liegt vollständig bei `DomainEntityActor`. Die Entity kennt nur ihren eigenen Upstream-Target und startet den `HealthCheckActor`.

**State:**
- `UpstreamTarget _target`
- `IActorRef? _healthCheckActor`
- `Func<UpstreamTarget, Props> _healthCheckPropsFactory` (per Konstruktor injiziert, aus `SchleusenwerkActorSystemSetup`)

**Behavior:**

```
Ready (einziger Zustand, kein Stash nötig):
  RegisterUpstream          → _target aktualisieren
                              → HealthCheckActor per _healthCheckPropsFactory starten falls noch nicht läuft
  SelectUpstreamForDomain   → Sender.Tell(new UpstreamResolved(_target, msg.Config))
```

`UpstreamEntityActor` sendet kein `NoHealthyUpstream` zurück — `DomainEntityActor` filtert unhealthy Upstreams bereits vor dem Forward heraus.

`HealthCheckActor` (Kind) publiziert `UpstreamHealthChanged` direkt an den `EventHub` → `DomainEntityActor` empfängt es via StreamRef und pflegt `_unhealthyUrls`.

---

## Passivation

| Entity | Strategie |
|---|---|
| `DomainEntityActor` | Explizit per `RemoveDomain` → `PoisonPill` |
| `UpstreamEntityActor` | Akka-Idle-Passivation nach 5 Minuten. Wenn keine Domain mehr referenziert, kommen keine `SelectUpstreamForDomain`-Nachrichten → automatische Passivation. `HealthCheckActor` stirbt mit dem Parent. |

---

## Shard-Konfiguration

```csharp
builder.WithShardRegion<DomainEntityActor>(
    "domain-router",
    extractEntityId: msg => msg is IWithDomain m ? (m.Domain, msg) : (null, null),
    extractShardId:  msg => msg is IWithDomain m ? Math.Abs(m.Domain.GetHashCode() % 20).ToString() : null,
    new ShardOptions { PassivateIdleEntityAfter = TimeSpan.FromMinutes(5) });

builder.WithShardRegion<UpstreamEntityActor>(
    "upstream-pool",
    extractEntityId: msg => msg is IWithUrl m ? (m.Url, msg) : (null, null),
    extractShardId:  msg => msg is IWithUrl m ? Math.Abs(m.Url.GetHashCode() % 20).ToString() : null,
    new ShardOptions { PassivateIdleEntityAfter = TimeSpan.FromMinutes(5) });
```

Shard-Count: 20 (anpassbar; Faustregel 10× maximale Nodeanzahl).

---

## Änderungen an bestehenden Komponenten

| Komponente | Änderung |
|---|---|
| `ConfigurationPersistenceActor` | Sendet `SetRoute` pro Domain statt `UpdateRoutes([...])` |
| `ProxyRequestHandler` | Erhält `IActorRef` für `ShardRegion<DomainEntityActor>` statt `DomainRouterActor` |
| `SchleusenwerkActorSystemSetup` | `DomainRouterActor`-Registrierung → zwei `WithShardRegion`-Aufrufe |
| `DomainRouterActor` | Wird gelöscht |
| `LoadBalancerActor` | Wird gelöscht |
| `UpstreamRouteeActor` | Wird gelöscht |

---

## Testing

**Unit-Tests (kein Cluster):**
- `DomainEntityActor` direkt per `Props.Create(...)` instantiiert, `EventHub` und `_upstreamRegion` als `TestProbe`
- `UpstreamEntityActor` direkt per `Props.Create(...)`, `EventHub` als `TestProbe`
- Bestehende Szenarien aus `DomainRouterActorSpec` und `DomainRouterHealthSpec` werden in `DomainEntityActorSpec` und `DomainEntityHealthSpec` übertragen

**Nicht in Unit-Tests abgedeckt:**
- Cluster-Sharding-Routing (Akka-intern)
- Idle-Passivation-Timeout

**TestKit-Basisklasse:** `Akka.TestKit.Xunit.TestKit` (unverändert — kein Persistence nötig)
