<!-- maggus-id: 0cfb39e3-46c1-46df-8207-932ea6d08728 -->

# Feature 002: Core Proxy

## Introduction

Wire all the moving parts of the proxy into a functioning end-to-end system. Feature 001 established the correct project structure and SQLite persistence. This feature makes the proxy actually work: spawning `LoadBalancerActor` and `HealthCheckActor` instances per domain, routing incoming HTTP/S requests through them, and discovering containers automatically via Docker label watching.

Three major subsystems are currently implemented but not connected:
- `LoadBalancerActor` exists but is never spawned
- `HealthCheckActor` exists but is never spawned
- `ProxyRequestHandler` has its own inline round-robin counter that bypasses `LoadBalancerActor`
- `DockerDiscoveryActor` is entirely missing

### Architecture Context

- **Vision alignment:** The proxy's core job — route HTTPS traffic to healthy upstreams, discover Docker containers, keep health state current — is defined in the vision. This feature delivers all of it.
- **Components touched:** `Schleusenwerk` (all actor and middleware layers)
- **New patterns introduced:** Per-domain actor lifecycle supervised by `DomainRouterActor`; `DockerDiscoveryActor` watching the Docker socket via Docker.DotNet; Kestrel port 80/443 binding configuration

## Goals

- Every domain in the routing table has exactly one `LoadBalancerActor` and one `HealthCheckActor` per upstream — supervised, started, and stopped in response to EventHub domain lifecycle events
- Incoming HTTP requests are routed through `LoadBalancerActor` (not a hand-rolled counter)
- Kestrel listens on ports 80 and 443; HTTPS is terminated at the edge; HTTP on port 80 serves ACME challenges and redirects
- `DockerDiscoveryActor` performs a full container scan on startup and streams start/stop events; discovered routes are registered via `ConfigurationPersistenceActor`
- `HealthCheckActor` probes each upstream at the configured interval and publishes `UpstreamHealthChanged` to EventHub; `LoadBalancerActor` rebuilds its router group on health changes

## Tasks

### TASK-002-001: Per-domain actor lifecycle

**Description:** As a developer, I want `DomainRouterActor` to own and manage per-domain `LoadBalancerActor` and `HealthCheckActor` children so that every routed domain has live health monitoring and load-balanced upstream selection.

**Token Estimate:** ~90k tokens
**Predecessors:** none
**Successors:** TASK-002-002
**Parallel:** yes — can run alongside TASK-002-003

**Current state:**
- `DomainRouterActor` maintains `Dictionary<DomainName, RouteDefinition>` in memory
- `LoadBalancerActor` exists — handles `SelectUpstream`, `UpdateUpstreams`, `MarkUpstreamHealthy/Unhealthy`; never spawned
- `HealthCheckActor` exists — probes HTTP/TCP, tells `UpstreamHealthChanged` to EventHub; never spawned

**What changes:**
- `DomainRouterActor` becomes a supervisor. When `UpdateRoutes` arrives: for each new domain spawn a `LoadBalancerActor` child (`Props.Create(() => new LoadBalancerActor(upstreams))`); for each removed domain stop the child. Keep a `Dictionary<DomainName, IActorRef>` of load balancer children.
- `HealthCheckActor` children are managed by `LoadBalancerActor`: one `HealthCheckActor` per upstream in the group. When `UpdateUpstreams` arrives, start actors for new upstreams and stop actors for removed ones. Each `HealthCheckActor` is passed the EventHub `IActorRef` to publish `UpstreamHealthChanged`.
- `DomainRouterActor.Handle(ResolveUpstream)` — instead of returning `UpstreamResolved(RouteDefinition)` and letting the caller pick, ask the child `LoadBalancerActor` with `SelectUpstream`. `LoadBalancerActor` uses its `RoundRobinGroup` to reply with `UpstreamSelected(target)`. `DomainRouterActor` forwards the reply to the original `Sender`. This makes the request flow: `ProxyRequestHandler → DomainRouterActor → LoadBalancerActor → UpstreamRouteeActor → UpstreamSelected`.
- `DomainRouterActor.Handle(UpstreamHealthChanged)` forwards the event to the correct `LoadBalancerActor` child; `LoadBalancerActor` updates its `RoundRobinGroup` and also notifies its `HealthCheckActor` children of the change.
- `DomainRouterActor` still maintains `_unhealthyUpstreams` for its own routing decisions (to exclude domains where all upstreams are down from `ResolveUpstream` responses).
- `ProxyRequestHandler` is updated to use `UpstreamSelected(UpstreamTarget)` instead of its current `UpstreamResolved(RouteDefinition)` + inline counter. The `ConcurrentDictionary<DomainName, int>` counter is removed.

**Acceptance Criteria:**
- [ ] `DomainRouterActor` spawns exactly one `LoadBalancerActor` child per domain when routes are added
- [ ] `DomainRouterActor` stops the `LoadBalancerActor` child when a domain is removed
- [ ] `LoadBalancerActor` spawns one `HealthCheckActor` child per upstream on creation and `UpdateUpstreams`
- [ ] `LoadBalancerActor` stops `HealthCheckActor` children for removed upstreams
- [ ] `DomainRouterActor.Handle(ResolveUpstream)` delegates to child `LoadBalancerActor.Ask(SelectUpstream)` and forwards `UpstreamSelected` reply to the original sender
- [ ] `ProxyRequestHandler` receives `UpstreamSelected(UpstreamTarget)` — the `ConcurrentDictionary` counter is gone
- [ ] `HealthCheckActor` publishes `UpstreamHealthChanged` to EventHub; `DomainRouterActor` + `LoadBalancerActor` both react correctly
- [ ] Unit tests cover: domain added → child spawned; domain removed → child stopped; health change → `RoundRobinGroup` rebuilt; `ResolveUpstream` returns correct upstream via load balancer
- [ ] `dotnet build` zero errors

---

### TASK-002-002: Kestrel port configuration

**Description:** As an operator, I want Kestrel bound to ports 80 and 443 so that HTTPS traffic is terminated at the edge and port 80 handles ACME challenges and HTTP-to-HTTPS redirects.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-002-001
**Successors:** none
**Parallel:** no
**Model:** haiku

**Details:**
- Configure Kestrel in `Program.cs` / `SchleusenwerkActorSystemSetup` via `webBuilder.ConfigureKestrel(...)`:
  - Port 80: HTTP only, serves ACME `/.well-known/acme-challenge/` paths; all other requests redirect to HTTPS
  - Port 443: HTTPS with `ServerCertificateSelector` callback (stub returning self-signed cert until Feature 004)
  - Port 5000: plain HTTP for gRPC management (stub for Feature 003)
- Add `UseHttpsRedirection()` middleware; exclude ACME challenge path from redirect
- Self-signed certificate for port 443: generated in-process with `CertificateRequest` + `X509Certificate2.CreateSelfSigned` (temporary stub until Feature 004 replaces it with the SNI selector)
- Read port numbers from `IConfiguration` with hardcoded defaults (80, 443, 5000)

**Acceptance Criteria:**
- [ ] Kestrel binds to ports 80, 443, and 5000 on startup
- [ ] HTTP request to port 80 on a non-ACME path returns 301/302 redirect to HTTPS
- [ ] Port 443 accepts HTTPS connections (self-signed cert is acceptable — browsers will show a warning)
- [ ] Port 5000 accepts plain HTTP (no TLS, for gRPC)
- [ ] `ASPNETCORE_URLS` or Kestrel config overrides from `appsettings.json` are respected
- [ ] `dotnet run --project src/Schleusenwerk/Schleusenwerk.csproj` starts without errors

---

### TASK-002-003: DockerDiscoveryActor

**Description:** As an operator, I want the proxy to automatically register routes for Docker containers with `schleusenwerk.*` labels so that I don't have to add routes manually for containerized services.

**Token Estimate:** ~80k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-002-001

**Details:**
- Add `Docker.DotNet` NuGet package to `Schleusenwerk`
- `DockerDiscoveryActor` is a singleton local actor registered via `WithActors` in `SchleusenwerkActorSystemSetup`
- On `PreStart`: connect to Docker socket (`npipe:////./pipe/docker_engine` on Windows, `unix:///var/run/docker.sock` on Linux); send `StartDiscovery` to self
- `StartDiscovery` handler: list all running containers, parse labels, register routes via `ConfigurationPersistenceActor.Ask(AddDomain)` / `Ask(AddUpstream)` for each labeled container. Mark each upstream with source `Docker`. After scan: start listening to Docker events (start/stop/die).
- **Label parsing:** Read `schleusenwerk.domain`, `schleusenwerk.port`, `schleusenwerk.tls` (default: `letsencrypt`), `schleusenwerk.healthcheck.path`, `schleusenwerk.healthcheck.interval` per the label table in VISION.md
- **Container start event:** parse labels → `AddDomain` + `AddUpstream` (idempotent — `ConfigurationPersistenceActor` returns `ConfigurationCommandAck` or `ConfigurationCommandNack` "already exists"; both are acceptable)
- **Container stop/die event:** `RemoveUpstream` for that container's upstream; `RemoveDomain` only if no upstreams remain (check `GetDomainByName` first)
- **Conflict rule:** Docker-sourced routes take precedence over manual routes for the same domain (this is tracked via upstream source field — `ConfigurationPersistenceActor` already models this)
- **Reconnect:** if Docker socket connection drops, retry with exponential backoff (max 60s)
- Upstream URL constructed as `http://<container-ip>:<port>` using the container's first network IP

**Acceptance Criteria:**
- [x] `Docker.DotNet` package added to `Schleusenwerk.csproj`
- [x] `DockerDiscoveryActor` registered as a local singleton in `SchleusenwerkActorSystemSetup`
- [x] On startup, all running containers with `schleusenwerk.domain` label are registered via `ConfigurationPersistenceActor`
- [x] When a labeled container starts, its route is registered within 1 second
- [x] When a labeled container stops, its upstream is removed; domain is removed only if no upstreams remain
- [x] If Docker socket is unavailable at startup, actor logs a warning and retries (does not crash the system)
- [x] Label parsing unit tests: valid labels produce correct `DomainConfig` + `UpstreamTarget`; missing required labels produce a warning log and are skipped
- [x] `dotnet build` zero errors

---

### TASK-002-004: Integration smoke test

**Description:** As a developer, I want an end-to-end test that verifies an HTTP request is proxied to a real upstream so that I can confirm the full actor pipeline works together.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-002-001, TASK-002-002
**Successors:** none
**Parallel:** no

**Details:**
- Use `Microsoft.AspNetCore.TestHost` (`WebApplicationFactory`) to start the proxy in-process
- Start a minimal `HttpListener` or a second `TestHost` as the upstream on a random port
- Register the upstream via `ConfigurationPersistenceActor` directly (bypass Docker discovery)
- Send an HTTP GET to the proxy host header matching the registered domain
- Assert the request arrives at the upstream and the proxy returns the upstream's response body + status code
- Assert `X-Forwarded-For` and `X-Forwarded-Host` headers are set

**Acceptance Criteria:**
- [ ] Test class in `Schleusenwerk.Tests/Integration/ProxyPipelineSpec.cs`
- [ ] Uses `WebApplicationFactory<Program>` (or equivalent) — no manual `ActorSystem.Create`
- [ ] Upstream receives the forwarded request with correct headers
- [ ] Proxy returns upstream's response body and status code unchanged
- [ ] Test passes reliably (no flakiness from timing) — uses `Ask` timeouts, not `Thread.Sleep`
- [ ] `dotnet test` passes including this test

---

## Task Dependency Graph

```
TASK-002-001 ──→ TASK-002-002 ──→ TASK-002-004
TASK-002-003 ────────────────────────────────── (independent, no downstream deps in this feature)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-002-001 | ~90k | none | yes (with 003) | — |
| TASK-002-002 | ~25k | 001 | no | haiku |
| TASK-002-003 | ~80k | none | yes (with 001) | — |
| TASK-002-004 | ~40k | 001, 002 | no | — |

**Total estimated tokens:** ~235k

## Functional Requirements

- FR-1: For every domain in the routing table, exactly one `LoadBalancerActor` child exists under `DomainRouterActor` and is stopped when the domain is removed
- FR-2: For every upstream in a domain, exactly one `HealthCheckActor` child exists under `LoadBalancerActor`; it probes the upstream at the configured interval and publishes `UpstreamHealthChanged` to EventHub
- FR-3: `ProxyRequestHandler` resolves upstreams via `DomainRouterActor → LoadBalancerActor → UpstreamRouteeActor`; no inline round-robin counter
- FR-4: Kestrel binds to ports 80 (HTTP), 443 (HTTPS, self-signed stub), and 5000 (plain HTTP for gRPC)
- FR-5: HTTP requests on port 80 for non-ACME paths are redirected to HTTPS
- FR-6: `DockerDiscoveryActor` scans all running containers on startup and registers labeled ones as routes
- FR-7: `DockerDiscoveryActor` reacts to Docker start/stop events within 1 second; adds/removes upstreams accordingly
- FR-8: If the Docker socket is unavailable, `DockerDiscoveryActor` retries with exponential backoff and does not crash the actor system

## Non-Goals

- No Let's Encrypt / ACME certificate issuance — that is Feature 004; port 443 uses a self-signed stub
- No gRPC service implementation — that is Feature 003; port 5000 is reserved but empty
- No Blazor UI wiring — that is Feature 005
- No per-domain health check configuration from the UI — health check config comes from Docker labels or hardcoded defaults for now
- No weighted load balancing — round-robin only for MVP
- No IPv6 upstream addresses

## Technical Considerations

- `DomainRouterActor` already handles `UpstreamHealthChanged` to maintain `_unhealthyUpstreams`. This logic should remain for the "all upstreams down" guard, but individual health events must also propagate to `LoadBalancerActor` children for `RoundRobinGroup` rebuilds.
- `LoadBalancerActor` uses Akka.NET `RoundRobinGroup` — routee paths are derived from `UpstreamRouteeActor` child refs. When upstreams change, call `Context.ActorOf(group.Props(), routerName)` to replace the router. Stop the old router before creating the new one.
- `HealthCheckActor` accepts a `probeFunc` in its constructor — inject `IHttpClientFactory` or a plain `HttpClient` for HTTP probes; use `TcpClient` for TCP. The existing implementation in `HealthCheckActor.cs` is the authoritative design — do not rewrite it, just wire it up.
- Docker socket path: `unix:///var/run/docker.sock` on Linux (production), `npipe:////./pipe/docker_engine` on Windows (dev). Detect via `RuntimeInformation.IsOSPlatform`.
- `WebApplicationFactory` in tests requires `Program` to be accessible — ensure `Program.cs` uses top-level statements with `public partial class Program {}` at the bottom, or use `InternalsVisibleTo`.
- `Akka.Persistence.TestKit` auto-configures test journal — TASK-002-004 integration test uses `WebApplicationFactory`, NOT `PersistenceTestKit`. The test actor system is bootstrapped by `WebApplicationFactory`.

## Success Metrics

- `dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj` — all tests pass including the integration smoke test
- `dotnet run --project src/Schleusenwerk/Schleusenwerk.csproj` starts without errors, Kestrel logs show bindings on 80, 443, 5000
- Starting a Docker container with `schleusenwerk.domain=test.local schleusenwerk.port=8080` labels causes the proxy to log "Domain added: test.local"

## Open Questions

*(none)*
