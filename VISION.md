# Schleusenwerk

A self-hosted HTTPS reverse proxy with automatic Let's Encrypt certificates, Docker container discovery, and a Blazor management UI. Built for developers and teams running Docker-based services who want a more extensible, UI-driven alternative to https-portal.

## Deployment Model

- **Proxy container** — a single Kestrel instance. Runs the edge proxy, handles TLS termination, routes traffic, watches the local Docker socket, and exposes a gRPC API for management.
- **Management UI container** — a separate Blazor Server application. Connects to the proxy via gRPC (request/response + server-side streaming for real-time events). No Akka cluster membership — just a gRPC client.
- **SQLite** — embedded database backing Akka.Persistence (journal + snapshot store). No external DB dependency.
- **Docker volume** — certificates (Let's Encrypt, self-signed, custom) are stored as files on a mounted volume. No cert data in the database.

Single-instance proxy — no HA clustering for MVP.

## Core Concepts

### Route

The primary resource. A mapping from a **domain/hostname** (e.g. `app.example.com`) to one or more **upstream targets** (e.g. `http://my-service:8080`).

- Each route has a TLS mode, health check configuration, and one or more upstream addresses
- A route can originate from two sources: **Docker labels** or **manual creation** via the UI
- When both sources define the same domain, **Docker labels win** — they override the manual route
- When a Docker container with route labels stops, its route is automatically removed

### Docker Discovery

The proxy watches the **Docker socket** for containers with specific labels. Discovery runs a full scan on startup and then watches for start/stop events.

**Labels:**

| Label | Required | Default | Description |
|-------|----------|---------|-------------|
| `schleusenwerk.domain` | yes | — | Target domain (e.g. `app.example.com`) |
| `schleusenwerk.port` | yes | — | Upstream port on the container |
| `schleusenwerk.tls` | no | `letsencrypt` | TLS mode: `letsencrypt`, `selfsigned`, `custom` |
| `schleusenwerk.healthcheck.path` | no | — | HTTP health check path |
| `schleusenwerk.healthcheck.interval` | no | — | Health check interval (e.g. `30s`) |

### TLS / Certificates

Three modes per route:

- **Automatic Let's Encrypt** (ACME v2, HTTP-01 challenge) — the default
- **Self-signed** — for local development or internal services
- **Custom certificate** — user uploads their own cert + key

Certificates are stored as **files on a mounted Docker volume**. Kestrel uses SNI (`ServerCertificateSelector`) to serve the correct certificate per domain. Renewal failure triggers an alert in the Management UI.

### Health Checks

- Configurable per route (HTTP probe or TCP connect)
- Unhealthy upstreams are removed from load balancer rotation until they recover
- Configurable interval and failure thresholds — details to be defined

### Load Balancing

- Multiple upstreams per route distributed via **Akka.NET round-robin routing**
- Only healthy upstreams participate in rotation

## Management UI

A separate Blazor Server container connected to the proxy via **gRPC**:

- **Request/response** for CRUD operations (routes, certs, config)
- **Server-side streaming** for real-time events (route discovered, cert renewed, health changed)
- Real-time push to the browser via SignalR (Blazor Server)

Features:

- View all active routes (source: manual vs. Docker-discovered)
- Create, edit, delete manual routes
- View upstream health status per route
- View certificate status (valid until, next renewal)
- Trigger manual certificate renewal

**Authentication:** none for MVP — access controlled via network isolation.

## Technology Stack

- **Edge proxy:** ASP.NET Core / Kestrel
- **Actor system:** Akka.NET with Akka.Hosting (C# config, no HOCON)
- **Upstream forwarding:** TurboHTTP
- **Persistence:** Akka.Persistence backed by SQLite
- **TLS:** Kestrel SNI + Certes (ACME v2), certs on Docker volume
- **UI:** Blazor Server (separate container), gRPC client + SignalR for browser push
- **Management API:** gRPC (request/response + server streaming)
- **Container discovery:** Docker socket API
