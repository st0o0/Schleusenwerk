# Proxy Architecture Redesign

## Overview

Restructure the Schleusenwerk actor system to simplify the request path, deduplicate health checks, restrict EventHub to UI concerns, and add resilience, rate limiting, mTLS, and observability.

Three phases, each independently testable and deployable.

---

## Phase 1: Actor Redesign

### 1.1 Merge DomainEntityActor + UpstreamEntityActor

The UpstreamEntityActor is eliminated. DomainEntityActor becomes the sole owner of upstream state.

**Changes:**
- Remove the `upstream-pool` ShardRegion from `SchleusenwerkActorSystemSetup`
- Remove `RegisterUpstream` Tells to the UpstreamRegion — DomainEntityActor manages upstreams inline
- Delete `UpstreamEntityActor.cs` and `UpstreamEntityEvents.cs`
- `SelectUpstreamForDomain` message becomes obsolete — `ResolveUpstream` already handles this in DomainEntityActor
- Existing persistence events (`DomainUpstreamAdded`, `DomainUpstreamRemoved`) remain unchanged — they already capture the full upstream state
- PersistenceId schema `domain-{name}`, recovery, and round-robin logic stay the same
- Orphaned `upstream-*` journal entries are left in place (inert, no migration needed)

### 1.2 HealthCheck ShardRegion

New ShardRegion `health-check` with EntityId derived from the upstream URL hash.

**HealthCheckEntityActor** (renamed from HealthCheckActor):
- `ReceiveActor` (not persistent — health state is transient, re-probed after restart)
- ShardRegion entity, no longer a child actor of UpstreamEntityActor
- Maintains `HashSet<IActorRef> _subscribers` for interested DomainEntityActors
- Uses `DeathWatch` on subscribers — automatic removal on `Terminated`

**Messages:**
- `SubscribeHealth(IActorRef subscriber)` — DomainEntityActor registers interest
- `UnsubscribeHealth(IActorRef subscriber)` — DomainEntityActor deregisters
- `CheckHealth` — periodic timer tick (existing behavior)
- `GetHealthStatus` — query (existing behavior)
- `UpstreamHealthChanged(UpstreamUrl, bool IsHealthy)` — Telled directly to each subscriber AND published to EventHub via SinkRef for UI

**DomainEntityActor integration:**
- On recovery/startup: `SubscribeHealth(Self)` to the HealthCheck ShardRegion for each known `UpstreamTarget`
- On `AddUpstream`: send `SubscribeHealth(Self)` to HealthCheck ShardRegion
- On `RemoveUpstream`: send `UnsubscribeHealth(Self)` to HealthCheck ShardRegion
- Receives `UpstreamHealthChanged` directly via Tell from HealthCheckEntityActor

### 1.3 EventHub — UI Bridge Only

EventHub keeps its MergeHub→BroadcastHub pipeline and the SinkRef handshake (`Ask<PublisherReady>(GetPublisher)`).

**What changes:**
- Actor-to-actor communication (health changes → DomainEntityActor) uses direct Tell, not EventHub
- Fewer actors need the SinkRef: only DomainEntityActor and HealthCheckEntityActor for UI-relevant events
- The `WaitingForPublisher` state remains in DomainEntityActor and HealthCheckEntityActor for UI event publication

**Events published to EventHub (for UI):**
- `DomainConfigured`, `DomainDeactivated`, `DomainUpstreamAdded`, `DomainUpstreamRemoved` — from DomainEntityActor
- `UpstreamHealthChanged` — from HealthCheckEntityActor
- `CertificateProvisioningRequested` — from DomainEntityActor

**HealthCheckEntityActor dual-path:**
- On health change: Tell `UpstreamHealthChanged` to each subscriber (fast path for routing)
- On health change: publish same event via SinkRef to EventHub (UI path)

---

## Phase 2: Resilience

### 2.1 Circuit Breaker in DomainEntityActor

Per-upstream circuit state tracked in-memory within the DomainEntityActor.

**State per upstream:**
```
UpstreamCircuitState {
    UpstreamUrl Url
    CircuitStatus Status        // Closed, Open, HalfOpen
    int ConsecutiveFailures
    DateTime? OpenedAt
    TimeSpan CooldownPeriod     // default 30s, configurable in DomainConfig
}
```

**State transitions:**
- **Closed** (normal): upstream participates in round-robin. Failures are counted.
- **Open**: upstream excluded from round-robin. After `CooldownPeriod` expires → HalfOpen.
- **HalfOpen**: next request goes as probe to this upstream. Success → Closed. Failure → Open (doubled cooldown, max 5min).

**Triggers:**
- `RequestFailed(UpstreamUrl)` — fire-and-forget Tell from ProxyDispatcher after 502/504. Increments `ConsecutiveFailures`. At threshold (default 3) → Open.
- `RequestSucceeded(UpstreamUrl)` — fire-and-forget Tell from ProxyDispatcher after success. Resets to Closed if HalfOpen, resets failure count if Closed.
- `UpstreamHealthChanged(url, isHealthy=false)` from HealthCheckEntityActor → immediate Open.
- `UpstreamHealthChanged(url, isHealthy=true)` from HealthCheckEntityActor → reset to Closed.

**No persistence** — circuit state is transient. After actor restart everything starts Closed, HealthCheck corrects quickly.

### 2.2 Adaptive Timeouts in ProxyDispatcher

- Ask timeout for domain resolution: configurable via app config (default 3s). This is only the actor lookup time, not the upstream request.
- Upstream request timeout: already uses `DomainConfig.RequestTimeout` (via `_sharedClient.Timeout = config.RequestTimeout`).
- New: `DomainConfig` gains an optional `ConnectTimeout` (default 5s), separate from `RequestTimeout` (default 30s).

### 2.3 Rate Limiting — Per Client-IP Per Domain

ASP.NET `RateLimiting` middleware with a custom partition policy.

**Partition key:** `(DomainName, ClientIP)`. Each combination gets its own token bucket or sliding window.

**Configuration in DomainConfig:**
```
RateLimitConfig {
    int RequestsPerWindow       // default 100
    TimeSpan Window             // default 60s
}
```

**Flow:**
1. Rate limiting middleware sits before ProxyDispatcher in the pipeline
2. Extracts domain from `Host` header, client IP from connection (respects `X-Forwarded-For` when `TrustedProxies` are configured)
3. Checks rate limit state (in-memory, `ConcurrentDictionary`-based)
4. On limit exceeded: `429 Too Many Requests` with `Retry-After` header
5. Rate limit config loaded from a cache fed by DomainEntityActor (DomainEntityActor Tells config changes to a `RateLimitConfigCache` singleton)

**No actor for rate limiting** — this is a hot path. In-memory state in the middleware is correct. Actor mailbox roundtrip per request would be too slow.

---

## Phase 3: Operations & Security

### 3.1 mTLS on Management Port

**Kestrel configuration:** Two separate endpoints.
- Proxy port (443/80): public, no client certificate required
- Management port (5000): `ClientCertificateMode.RequireCertificate`

**Implementation:**
- Trusted CA loaded from app config (path to CA certificate file)
- Custom `CertificateValidation` callback on the management endpoint — validates issuer against trusted CA, optional subject/thumbprint whitelist
- Blazor UI runs on the management port — automatically protected
- gRPC management service runs on the management port — automatically protected

**Client setup:** gRPC clients (CLI, external tools) need a client certificate signed by the trusted CA. A self-signed CA can be generated on first start for internal use, or an external CA certificate is configured.

### 3.2 OpenTelemetry — Proxy Metrics

**Meter:** `Schleusenwerk.Proxy`

| Instrument | Type | Tags | Purpose |
|---|---|---|---|
| `proxy.requests` | Counter | domain, status_code | Request volume |
| `proxy.request.duration` | Histogram | domain, upstream_url | Latency distribution |
| `proxy.upstream.health` | UpDownCounter | upstream_url | Currently healthy/unhealthy count |
| `proxy.circuit_breaker.trips` | Counter | domain, upstream_url | Circuit breaker activations |
| `proxy.rate_limit.rejected` | Counter | domain, client_ip | Rejected requests |

**Integration points:**
- `ProxyDispatcher.HandleAsync`: increment `proxy.requests`, record `proxy.request.duration`
- `DomainEntityActor`: update `proxy.upstream.health` on `UpstreamHealthChanged`, increment `proxy.circuit_breaker.trips` on state change to Open
- Rate limiting middleware: increment `proxy.rate_limit.rejected`

---

## Decisions Log

| Decision | Choice | Rationale |
|---|---|---|
| Health subscription model | DomainEntityActor subscribes at HealthCheckEntityActor | Explicit, no inverse lookup needed |
| Circuit breaker location | DomainEntityActor (state-based) | Central upstream state, fire-and-forget Tell from ProxyDispatcher |
| Rate limiting granularity | Per client-IP per domain | Protects individual domains from single-client floods |
| gRPC auth method | mTLS (mutual TLS) | No application-level auth code, Kestrel-native |
| Observability scope | Proxy metrics only (no tracing, no actor telemetry) | Minimal viable observability, tracing added later |
| Actor-to-actor communication | Direct Tell | Explicit dependencies, no global event bus |
| EventHub SinkRef handshake | Keep | Proven pattern for backpressured UI event publishing |
| Persistence migration | None (orphaned journal entries left in place) | Inert data, DomainEntityActor has complete state from own events |
| Phasing | 3 phases: actor redesign → resilience → ops/security | Each phase independently testable, clear rollback |
