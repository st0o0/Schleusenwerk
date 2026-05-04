# Architecture

## Overview

Schleusenwerk is a single-instance HTTPS reverse proxy built on ASP.NET Core / Kestrel, Akka.NET, and TurboHTTP. A separate Vue.js SPA (served by nginx) connects to the proxy via a REST API for management. Live events are pushed to the UI via SignalR WebSocket. State is persisted locally via Akka.Persistence backed by SQLite. No external database, no Akka cluster for MVP.

## Solution Structure

| Project | Role |
|---------|------|
| `Schleusenwerk` | Proxy — Kestrel edge, TLS termination, TurboHTTP forwarding, Docker discovery, Akka actors, REST management API, SignalR hub |
| `Schleusenwerk.Web` | Management UI — Vue 3 SPA, PrimeVue (Unstyled), Pinia stores, Blueprint theme, served by nginx |
| `Schleusenwerk.Core` | Shared contracts — message types, domain models, interfaces |
| `Schleusenwerk.Contracts` | API contract — `openapi.yaml` (single source of truth), NSwag config |
| `Schleusenwerk.Tests` | Unit tests |
| `Schleusenwerk.IntegrationTests` | Integration tests |
| `Schleusenwerk.AppHost` | Aspire orchestration for local development |

## Backend

### Runtime & Startup

- **Proxy:** ASP.NET Core with Kestrel; Akka.Hosting for actor system bootstrap
- **Akka configuration:** C# API only via `AkkaConfigurationBuilder` — no HOCON
- **Persistence:** Akka.Persistence with SQLite journal and snapshot store
- **Management API:** REST/JSON controllers on port 5000, SignalR hub at `/hubs/events`

### Key Actors

| Actor | Scope | Responsibility |
|-------|-------|----------------|
| **EventHub** | Singleton | Akka.Streams MergeHub → BroadcastHub pipeline. Intra-actor pub/sub bus. Publishers obtain an `ISinkRef` via `GetPublisher`; subscribers receive an `ISourceRef` via `Subscribe`. Low-frequency publishers may also `Tell` directly. |
| **DomainEntityActor** | Sharded (per domain) | Event-sourced. Owns domain config, upstream list, and health state. Persists to SQLite via Akka.Persistence. Publishes `IClusterEvent` to EventHub on every state change. Resolves incoming requests to healthy upstreams (round-robin). |
| **HealthCheckEntityActor** | Sharded (per upstream) | Runs configurable HTTP GET probes. On health state change publishes `UpstreamHealthChanged` to EventHub. |
| **CertificateProvisioningActor** | Singleton | Manages TLS certificate lifecycle — ACME v2 via Certes, self-signed generation, custom cert storage on Docker volume. |
| **DockerDiscoveryActor** | Singleton | Watches local Docker socket via Docker Engine API. Full container scan on startup, then monitors start/stop events. Parses `schleusenwerk.*` labels and forwards route registrations to `DomainEntityActor` region. |

### Message Flow

**Incoming HTTPS request:**
```
Kestrel → ProxyDispatcher
  → DomainEntityActor.Ask(ResolveUpstream(host))
  → DomainEntityActor: round-robin select from healthy upstreams → UpstreamResolved
  → RequestForwardingPipeline: forward via TurboHTTP
```

**Health state change:**
```
HealthCheckEntityActor → EventHub.Tell(UpstreamHealthChanged)
                       → BroadcastHub fans out to all subscribers
                       → DomainEntityActor: updates unhealthy upstream set
                       → EventBridgeService → SignalR → Vue.js UI
```

**Route created / updated:**
```
REST API call → RouteController → IConfigurationService
              → DomainEntityActor.Ask(AddDomain / UpdateDomain)
              → persist event → apply to state
              → EventHub: DomainConfigured
              → EventBridgeService → SignalR → Vue.js UI
```

**Docker container discovered:**
```
DockerDiscoveryActor → DomainEntityActor region (AddDomain / AddUpstream)
                     → EventHub → EventBridgeService → SignalR → Vue.js UI
```

**Docker container stops:**
```
DockerDiscoveryActor → DomainEntityActor region (RemoveDomain / RemoveUpstream)
                     → Docker-sourced upstreams removed
                     → manual upstreams for same domain resurface
```

**Route conflict resolution:**
- Each domain tracks the **source** of each upstream (Docker vs. manual)
- Docker-sourced routes override manual routes for the same domain
- When a Docker container stops, only its upstreams are removed

## Management API (REST + SignalR)

The proxy exposes a **REST API** on port 5000 consumed by the Vue.js SPA via nginx reverse proxy.

### OpenAPI Contract

The API is defined contract-first in `src/Schleusenwerk.Contracts/openapi.yaml`. NSwag generates:
- C# abstract base controllers → implemented in `Schleusenwerk/Controllers/`
- TypeScript client → consumed by Pinia stores in `Schleusenwerk.Web/`

### Endpoints

| Area | Method | Path | Description |
|------|--------|------|-------------|
| Routes | GET | `/api/routes` | List all routes |
| | GET | `/api/routes/{domain}` | Route detail with health |
| | POST | `/api/routes` | Create route |
| | PUT | `/api/routes/{domain}` | Update route config |
| | DELETE | `/api/routes/{domain}` | Delete route |
| | POST | `/api/routes/{domain}/upstreams` | Add upstream |
| | DELETE | `/api/routes/{domain}/upstreams/{encodedUrl}` | Remove upstream (base64url-encoded) |
| Certificates | GET | `/api/certificates` | List certificates |
| | GET | `/api/certificates/{domain}` | Certificate detail |
| | POST | `/api/certificates/{domain}/provision` | Trigger renewal |
| Health | GET | `/api/health` | Overall proxy status |
| | GET | `/api/health/{domain}` | Upstream health per domain |

### Live Events (SignalR)

- **Hub:** `/hubs/events` on port 5000
- **EventBridgeService** (BackgroundService) subscribes to Akka EventHub, maps `IClusterEvent` to `ProxyEventDto`, broadcasts via SignalR
- Vue.js connects via `@microsoft/signalr` with automatic reconnect
- Event types: `RouteUpdated`, `RouteRemoved`, `UpstreamHealthChanged`, `CertificateProvisioned`, `CertificateExpiring`

### Controllers

| Controller | Delegates to | Description |
|------------|-------------|-------------|
| `RouteController` | `IConfigurationService` | Route CRUD + upstream management |
| `CertificateController` | `ICertificateStore` + `EventHub` | Certificate queries + provisioning trigger |
| `HealthController` | `IConfigurationStore` + `DomainEntityActor` | Health aggregation via actor Ask |

## Forwarding Layer

- **`RequestForwardingPipeline`** — translates Kestrel `HttpContext` to an upstream request via TurboHTTP; streams the response back without buffering; sets `X-Forwarded-For`, `X-Real-IP`, `X-Forwarded-Proto`, `X-Forwarded-Host`; returns 502 on connection failure, 504 on timeout
- **`WebSocketTunnel`** — upgrades connection and bidirectionally tunnels WebSocket frames to the upstream
- **`HeaderManipulationFilter`** — strips hop-by-hop headers; applies per-route header injection rules

## Data & Persistence

- **SQLite** — embedded, no external dependency; file stored on a mounted Docker volume
- **Akka.Persistence journal + snapshot store** — all writes go through actors, no direct DB access
- **Per-domain state (`DomainEntityActor`):** domain config, upstream list, health status
- **Global state (`IConfigurationStore`):** proxy settings, domain configs (SQLite read model)
- **Certificates** — stored as files on a mounted Docker volume; not in the database
- **Snapshots** — taken every N events (configurable); old snapshots pruned automatically

## TLS / Certificate Management

- **Kestrel SNI** — `ServerCertificateSelector` callback resolves the certificate per domain at TLS handshake time
- **Let's Encrypt** — ACME v2 via Certes, HTTP-01 challenge; default mode
- **Self-signed** — generated in-process for dev/internal routes
- **Custom** — user-uploaded cert + key stored on Docker volume
- **Renewal failure** — `CertificateProvisioningActor` retries with exponential backoff; alert pushed to UI via SignalR
- **Port 80** — serves ACME HTTP-01 challenges only

## Frontend (Management UI)

- **`Schleusenwerk.Web`** — Vue 3 + Vite + TypeScript SPA served by nginx in a separate container
- **PrimeVue (Unstyled)** — headless components styled via Blueprint theme CSS Passthrough API
- **Pinia** — state management; stores encapsulate API calls, pages only inject stores
- **Vue Router** — 8 pages with Schleusen-Terminologie navigation
- **SignalR** — `@microsoft/signalr` client for live events (Flussprotokoll page)
- **Blueprint Theme** — dark theme with monospace typography, Schleusen-Terminologie throughout

### Pages

| Route | Page | Description |
|-------|------|-------------|
| `/` | Leitstand | Dashboard with KPI cards and system status |
| `/tore` | Schleusentore | Route list with status, source, cert icons |
| `/tore/neu` | Tor einsetzen | Create route form |
| `/tore/:domain` | Tor-Detail | Route config + upstream management |
| `/siegel` | Siegel | Certificate list with renewal |
| `/flussprotokoll` | Flussprotokoll | Live event stream via SignalR |
| `/hafenbecken` | Hafenbecken | Docker discovery (placeholder — API not yet implemented) |
| `/stellwerk` | Stellwerk | Settings and system info |

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

- Configurable per upstream: HTTP GET to a path
- Managed by `HealthCheckEntityActor` shards, one per upstream
- State propagated via EventHub; unhealthy upstreams excluded from routing immediately

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
    networks:
      - schleusenwerk

  web:
    image: schleusenwerk-web
    ports:
      - "3000:80"
    networks:
      - schleusenwerk
    depends_on:
      - proxy

volumes:
  certs:
  db:
```

**Important:** Port 5000 (management API) is NOT exposed to the host in production. The nginx in the web container reverse-proxies `/api/*` and `/hubs/*` to `http://proxy:5000` within the Docker network. No authentication on the management API for MVP — security relies on network isolation.

For local development, port 5000 can be mapped for direct API access.

### Configuration

- **SQLite path, ACME contact email, OTLP endpoint:** environment variables
- **No external database** — SQLite file on mounted Docker volume
- **No cluster configuration** — single-instance proxy for MVP

## Observability

- **Traces:** per-request spans (Kestrel → DomainEntityActor → TurboHTTP → response) via OpenTelemetry
- **Metrics:** request counters, latency histograms, health check results, certificate expiry — exported via OpenTelemetry
- **Logs:** structured logging via `Microsoft.Extensions.Logging`, exported via OpenTelemetry
- **Exporter:** OTLP (compatible with Grafana, Jaeger, Datadog, etc.)
