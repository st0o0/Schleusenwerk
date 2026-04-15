# Architecture

## Overview

Schleusenwerk is a single-instance HTTPS reverse proxy built on ASP.NET Core / Kestrel, Akka.NET, and TurboHTTP. A separate Blazor Server container connects to the proxy via gRPC for management. State is persisted locally via Akka.Persistence backed by SQLite. No external database, no Akka cluster for MVP.

## Solution Structure

| Project | Role |
|---------|------|
| `Schleusenwerk` | Proxy — Kestrel edge, TLS termination, TurboHTTP forwarding, Docker discovery, Akka actors |
| `Schleusenwerk.UI` | Management UI — Blazor Server, gRPC client, SignalR for browser push |
| `Schleusenwerk.Core` | Shared contracts — message types, domain models, interfaces |
| `Schleusenwerk.Tests` | Unit and integration tests |

## Backend

### Runtime & Startup

- **Proxy:** ASP.NET Core with Kestrel; Akka.Hosting for actor system bootstrap
- **Akka configuration:** C# API only via `AkkaConfigurationBuilder` — no HOCON
- **Persistence:** Akka.Persistence with SQLite journal and snapshot store
- **gRPC:** management endpoint exposed by the proxy, consumed by the UI container

### Key Actors

| Actor | Scope | Responsibility |
|-------|-------|----------------|
| **EventHub** | Singleton | Akka.Streams MergeHub → BroadcastHub pipeline. Intra-actor pub/sub bus. Publishers obtain an `ISinkRef` via `GetPublisher`; subscribers receive an `ISourceRef` via `Subscribe`. Low-frequency publishers may also `Tell` directly. |
| **ConfigurationPersistenceActor** | Singleton | Event-sourced. Owns all domain config, upstream lists, and proxy settings. Persists to SQLite. Publishes `IClusterEvent` to EventHub on every state change. Validates commands before persisting. |
| **DomainRouterActor** | Singleton | In-memory routing table (`DomainName → RouteDefinition`). Subscribes to EventHub for `UpstreamHealthChanged`. Resolves incoming `Host` header to a healthy `UpstreamTarget` (exact match first, then wildcard). |
| **LoadBalancerActor** | One per domain | Distributes requests across healthy upstreams via Akka.NET `RoundRobinGroup`. Rebuilds the router group whenever health state changes or the upstream list is updated. Upstream weight is honoured by creating proportional `UpstreamRouteeActor` children. |
| **UpstreamRouteeActor** | Child of LoadBalancerActor | Represents one weighted upstream slot. Receives `SelectUpstream` and replies with the upstream's address. |
| **HealthCheckActor** | One per upstream | Runs configurable HTTP GET or TCP connect probes. On health state change publishes `UpstreamHealthChanged` to EventHub. |
| **CertificateRenewalActor** | One per domain | Manages TLS certificate lifecycle — ACME v2 via Certes, self-signed generation, custom cert storage on Docker volume. Retries with exponential backoff on failure. — *to be implemented* |
| **DockerDiscoveryActor** | Singleton | Watches local Docker socket via Docker Engine API. Full container scan on startup, then monitors start/stop events. Parses `schleusenwerk.*` labels and forwards route registrations to `ConfigurationPersistenceActor`. — *to be implemented* |

### Message Flow

**Incoming HTTPS request:**
```
Kestrel → ProxyRequestHandler
  → DomainRouterActor.Ask(ResolveUpstream(host))
  → DomainRouterActor: filter healthy upstreams → UpstreamResolved(route)
  → RequestForwardingPipeline: forward via TurboHTTP
```

**Health state change:**
```
HealthCheckActor → EventHub.Tell(UpstreamHealthChanged)
                 → BroadcastHub fans out to all subscribers
                 → DomainRouterActor: updates unhealthy upstream set
                 → LoadBalancerActor: rebuilds RoundRobinGroup
```

**Route created / updated:**
```
gRPC call → ConfigurationPersistenceActor.Ask(AddDomain / UpdateDomain)
          → persist event → apply to state
          → EventHub: DomainAdded / DomainUpdated
          → DomainRouterActor: UpdateRoutes
```

**Docker container discovered:**
```
DockerDiscoveryActor → ConfigurationPersistenceActor (AddDomain / AddUpstream)
                     → EventHub → DomainRouterActor, LoadBalancerActor
```

**Docker container stops:**
```
DockerDiscoveryActor → ConfigurationPersistenceActor (RemoveDomain / RemoveUpstream)
                     → Docker-sourced upstreams removed
                     → manual upstreams for same domain resurface
```

**Route conflict resolution:**
- Each domain tracks the **source** of each upstream (Docker vs. manual)
- Docker-sourced routes override manual routes for the same domain
- When a Docker container stops, only its upstreams are removed

## Forwarding Layer

- **`RequestForwardingPipeline`** — translates Kestrel `HttpContext` to an upstream request via TurboHTTP; streams the response back without buffering; sets `X-Forwarded-For`, `X-Real-IP`, `X-Forwarded-Proto`, `X-Forwarded-Host`; returns 502 on connection failure, 504 on timeout
- **`WebSocketTunnel`** — upgrades connection and bidirectionally tunnels WebSocket frames to the upstream
- **`HeaderManipulationFilter`** — strips hop-by-hop headers; applies per-route header injection rules

## Data & Persistence

- **SQLite** — embedded, no external dependency; file stored on a mounted Docker volume
- **Akka.Persistence journal + snapshot store** — all writes go through actors, no direct DB access
- **Per-domain state (`ConfigurationPersistenceActor`):** domain config, upstream list, proxy settings, ACME stage
- **Certificates** — stored as files on a mounted Docker volume; not in the database
- **Snapshots** — taken every N events (configurable); old snapshots pruned automatically
- **Config export** — `ConfigurationExporter` produces a `ConfigExportDocument` for backup/restore

## TLS / Certificate Management

- **Kestrel SNI** — `ServerCertificateSelector` callback resolves the certificate per domain at TLS handshake time
- **Let's Encrypt** — ACME v2 via Certes, HTTP-01 challenge; default mode
- **Self-signed** — generated in-process for dev/internal routes
- **Custom** — user-uploaded cert + key stored on Docker volume
- **Renewal failure** — `CertificateRenewalActor` retries with exponential backoff; alert pushed to Management UI via gRPC server streaming
- **Port 80** — serves ACME HTTP-01 challenges only

## Docker Discovery

- `DockerDiscoveryActor` watches the local Docker socket via Docker Engine API
- **Startup:** full container scan; registers routes for all labeled containers
- **Runtime:** event stream for container start / stop

**Labels:**

| Label | Required | Default | Description |
|-------|----------|---------|-------------|
| `schleusenwerk.domain` | yes | — | Target domain (e.g. `app.example.com`) |
| `schleusenwerk.port` | yes | — | Upstream port on the container |
| `schleusenwerk.tls` | no | `letsencrypt` | TLS mode: `letsencrypt`, `selfsigned`, `custom` |
| `schleusenwerk.healthcheck.path` | no | — | HTTP health check path |
| `schleusenwerk.healthcheck.interval` | no | — | Health check interval (e.g. `30s`) |

## Health Checks

- Configurable per route: HTTP GET to a path, or TCP connect
- Managed by `HealthCheckActor` children, one per upstream
- Configurable interval and failure threshold — *details to be defined*
- State propagated via EventHub; unhealthy upstreams excluded from routing immediately

## Management API (gRPC)

- The proxy exposes a **gRPC service** consumed by the Management UI container
- **Request/response** operations: CRUD for routes, upstreams, settings; certificate operations
- **Server-side streaming** for real-time events: route discovered, health changed, cert renewed
- UI container is a pure gRPC client — no Akka cluster membership

## Frontend (Management UI)

- **`Schleusenwerk.UI`** — Blazor Server in a separate container; connects to proxy gRPC endpoint
- Real-time browser updates via SignalR (Blazor Server default transport)
- **No REST API** — all communication via gRPC

### UI Features

- Route list with source indicator (manual / Docker-discovered)
- Route CRUD (manual routes only)
- Upstream health status per route
- Certificate status (expiry, next renewal, mode)
- Manual certificate renewal trigger

## Deployment

### Docker Compose (reference topology)

```yaml
services:
  proxy:
    image: schleusenwerk-proxy
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - certs:/certs
      - db:/data
    ports:
      - "443:443"
      - "80:80"
      - "5000:5000"   # gRPC management

  ui:
    image: schleusenwerk-ui   # Schleusenwerk.UI
    ports:
      - "8080:8080"
    environment:
      - PROXY_GRPC_ENDPOINT=http://proxy:5000

volumes:
  certs:
  db:
```

### Configuration

- **SQLite path, ACME contact email, OTLP endpoint:** environment variables
- **No external database** — SQLite file on mounted Docker volume
- **No cluster configuration** — single-instance proxy for MVP

## Observability

- **Traces:** per-request spans (Kestrel → DomainRouterActor → TurboHTTP → response) via OpenTelemetry
- **Metrics:** request counters, latency histograms, health check results, certificate expiry — exported via OpenTelemetry
- **Logs:** structured logging via `Microsoft.Extensions.Logging`, exported via OpenTelemetry
- **Exporter:** OTLP (compatible with Grafana, Jaeger, Datadog, etc.)
