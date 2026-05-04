# Schleusenwerk Docker Release Design

## Goal

Ship two production-ready Docker images (`ghcr.io/st0o0/schleusenwerk` and `ghcr.io/st0o0/schleusenwerk-web`) that serve as a 1:1 drop-in replacement for https-portal. Configuration via `DOMAINS` environment variable, automatic Let's Encrypt certificates, Docker container auto-discovery.

## Architecture

```
┌─────────────────────────────┐     ┌──────────────────────────┐
│  schleusenwerk (Proxy)      │     │  schleusenwerk-web (SPA) │
│  Kestrel                    │     │  Caddy                   │
│  :80  HTTP (Proxy + ACME)   │     │  :3000 SPA               │
│  :443 HTTPS (Proxy + SNI)   │     │  Static Vue.js Build     │
│  :5000 Management API       │◄────│  API Reverse-Proxy →5000 │
│  Akka.NET Actors            │     │                          │
│  SQLite, Lego, Certs        │     │                          │
└─────────────────────────────┘     └──────────────────────────┘
         ▲
         │ /var/run/docker.sock
         ▼
    Docker Engine
```

Two containers, one docker-compose. The proxy container handles all TLS termination, routing, and certificate management. The web container serves the Vue.js management UI via Caddy and reverse-proxies API calls to the proxy.

## Decisions

- **SPA serving:** Caddy in a second container (not nginx, not Kestrel embedded).
- **Management API security:** Network isolation only. Port 5000 is not exposed to the host, only accessible within the Docker network. Documentation warns against exposing it publicly.
- **Configuration:** `DOMAINS` environment variable only (https-portal compatible). UI is read-only monitoring, not a configuration tool.
- **Multi-arch:** `linux/amd64` + `linux/arm64` from day one.
- **Registry:** GitHub Container Registry (`ghcr.io/st0o0/schleusenwerk`, `ghcr.io/st0o0/schleusenwerk-web`).
- **Tagging:** `latest` + semver (`v1.0.0` produces tags `1.0.0`, `1.0`, `1`).

## Component: Proxy Container

### Dockerfile Changes

Current issues: missing port 443, no HEALTHCHECK, running as root, no VOLUME declarations.

**Required fixes:**
1. Add `EXPOSE 443` alongside existing 80 and 5000.
2. Add `HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD curl -sf http://localhost:5000/api/health || exit 1`.
3. Add `USER app` (the ASP.NET base image provides this user).
4. Add `VOLUME ["/data", "/certs"]` for persistence hints.
5. Install `curl` in the runtime image for healthcheck.

### Health Endpoint Improvement

Current `/api/health` returns hardcoded `200 "healthy"`. Must verify:
- SQLite database is reachable (query `proxy_settings`).
- Actor system is running (ask `DomainEntityActor` region with timeout).

The simple `/health` endpoint on ports 80/443 stays as-is (load balancer probe).

### Port Binding

| Port | Purpose | Exposed to Host |
|------|---------|-----------------|
| 80   | HTTP proxy traffic + ACME challenges | Yes |
| 443  | HTTPS proxy traffic with SNI | Yes |
| 5000 | Management API + SignalR hub | No (Docker-internal only) |

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `DOMAINS` | Yes | — | https-portal format: `domain -> http://upstream:port`, comma-separated |
| `STAGE` | No | `production` | `local` (self-signed), `staging` (LE test), `production` (LE live) |
| `ACME_EMAIL` | For production | — | Contact email for Let's Encrypt account |
| `DNS_PROVIDER` | No | — | Lego DNS provider name for DNS-01/wildcard certs |

### Persistence Volumes

| Path | Purpose |
|------|---------|
| `/data` | SQLite database (`schleusenwerk.db`) |
| `/certs` | TLS certificates (Let's Encrypt via Lego, self-signed, custom) |

## Component: Web Container

### New Dockerfile (`Schleusenwerk.Web/Dockerfile.prod`)

Two-stage build:
1. **Build stage:** Node 22-alpine, `npm ci && npm run build` produces `/app/dist`.
2. **Runtime stage:** `caddy:2-alpine`, copies `dist/` to `/srv`, copies `Caddyfile`.

`VITE_API_URL` is left empty — Caddy proxies `/api/*` and `/hubs/*` to the proxy container, so no CORS configuration is needed.

### Caddyfile

```
:3000 {
    root * /srv
    file_server
    try_files {path} /index.html

    handle /api/* {
        reverse_proxy proxy:5000
    }
    handle /hubs/* {
        reverse_proxy proxy:5000
    }

    handle /health {
        respond "ok" 200
    }

    encode gzip zstd
}
```

- SPA fallback via `try_files` for client-side routing.
- Reverse proxy for API and SignalR hub to the proxy container.
- Built-in gzip/zstd compression.
- Health endpoint at `/health` for Docker healthcheck.
- No HTTPS — the proxy container terminates TLS.

### Healthcheck

```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -qO- http://localhost:3000/health || exit 1
```

## Component: Docker Compose (Production)

```yaml
services:
  proxy:
    image: ghcr.io/st0o0/schleusenwerk:latest
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - certs:/certs
      - data:/data
    environment:
      - DOMAINS=example.com -> http://backend:8080
      - STAGE=production
      - ACME_EMAIL=admin@example.com
    restart: unless-stopped

  web:
    image: ghcr.io/st0o0/schleusenwerk-web:latest
    ports:
      - "3000:3000"
    restart: unless-stopped

volumes:
  certs:
  data:
```

Key properties:
- Port 5000 NOT exposed to host (only Docker-internal via `proxy:5000`).
- Docker socket mounted read-only for container discovery.
- Named volumes for certificates and database.
- `restart: unless-stopped` for resilience.

## Component: GitHub Actions CI/CD

### Workflow: `docker-build.yml`

**Triggers:**
- Push to `main` branch.
- Tags matching `v*` (e.g., `v1.0.0`).

**Jobs:**

1. **build-proxy**: Builds `schleusenwerk` image from `src/Schleusenwerk/Dockerfile`.
2. **build-web**: Builds `schleusenwerk-web` image from `src/Schleusenwerk.Web/Dockerfile.prod`.

Both jobs use:
- `docker/setup-qemu-action` for ARM64 emulation.
- `docker/setup-buildx-action` for multi-platform builds.
- `docker/build-push-action` with `platforms: linux/amd64,linux/arm64`.
- `docker/metadata-action` for semver tagging.
- `docker/login-action` with `GITHUB_TOKEN` for ghcr.io push.

**Tag strategy** (via `docker/metadata-action`):
- `main` push → `latest`
- `v1.2.3` tag → `1.2.3`, `1.2`, `1`, `latest`

### Workflow: `ci.yml` (prerequisite)

- Runs `dotnet build` + `dotnet test` on every PR and push.
- Blocks merge on failure.
- Runs `vue-tsc --noEmit` for TypeScript check on SPA.

## Component: README.md

Sections:
1. **What is Schleusenwerk** — one paragraph, https-portal replacement.
2. **Quick Start** — copy-paste docker-compose.yml, 5 lines to running proxy.
3. **Environment Variables** — table with `DOMAINS`, `STAGE`, `ACME_EMAIL`, `DNS_PROVIDER`.
4. **Docker Discovery** — label format (`schleusenwerk.domain`, `schleusenwerk.upstream`, `schleusenwerk.tls-mode`).
5. **Volumes** — `/data` and `/certs` explanation.
6. **Management UI** — how to access on port 3000, read-only monitoring.
7. **Migrating from https-portal** — env var format is identical, just change the image name.

## Out of Scope (Future Work)

- Authentication for Management API (currently network-isolation only).
- Live configuration changes via UI (currently env-var only).
- Kubernetes Helm chart.
- Rate limiting configuration via env vars.
- Custom error pages.
