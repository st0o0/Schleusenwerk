# Docker Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship two production-ready multi-arch Docker images (`ghcr.io/st0o0/schleusenwerk` and `ghcr.io/st0o0/schleusenwerk-web`) as a drop-in https-portal replacement.

**Architecture:** Proxy container (Kestrel on 80/443/5000) handles routing, TLS, ACME, and Docker discovery. Web container (Caddy on 3000) serves Vue SPA and reverse-proxies API calls to proxy:5000. Configuration via `DOMAINS` env var only. GitHub Actions builds and pushes multi-arch images to ghcr.io.

**Tech Stack:** .NET 10, Kestrel, Akka.NET, Caddy 2, Vue 3 + Vite, Docker Buildx, GitHub Actions

**Spec:** `docs/superpowers/specs/2026-05-04-docker-release-design.md`

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `src/Schleusenwerk/Dockerfile` | Modify | Add port 443, HEALTHCHECK, non-root user, VOLUME declarations |
| `src/Schleusenwerk.Web/Dockerfile.prod` | Create | Multi-stage: Node build + Caddy runtime |
| `src/Schleusenwerk.Web/Caddyfile` | Create | SPA serving, API reverse proxy, health endpoint |
| `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs` | Modify | Improve `/health` endpoint to verify DB + actor system |
| `docker-compose.prod.yml` | Rewrite | Production-ready with proper ports, volumes, restart policy |
| `docker-compose.yml` | Modify | Dev compose: use Caddy-based web container, fix port 5000 exposure |
| `.github/workflows/docker-build.yml` | Create | Multi-arch image build + push to ghcr.io |
| `.github/workflows/ci.yml` | Create | Build + test on PR/push |
| `README.md` | Rewrite | Quick-start, env var reference, migration guide |

---

### Task 1: Fix Proxy Dockerfile

**Files:**
- Modify: `src/Schleusenwerk/Dockerfile`

- [ ] **Step 1: Update Dockerfile with production fixes**

Replace the entire `src/Schleusenwerk/Dockerfile` with:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /build

COPY src/ ./src/

RUN dotnet restore ./src/Schleusenwerk/Schleusenwerk.csproj && \
    dotnet publish ./src/Schleusenwerk/Schleusenwerk.csproj \
        --configuration Release \
        --output /app \
        --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=goacme/lego /lego /usr/local/bin/lego

WORKDIR /app

COPY --from=build /app .

RUN mkdir -p /tmp/acme-webroot/.well-known/acme-challenge && \
    mkdir -p /certs/lego && \
    mkdir -p /data && \
    chown -R app:app /tmp/acme-webroot /certs /data

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80;https://+:443;http://+:5000

EXPOSE 80 443 5000

VOLUME ["/data", "/certs"]

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -sf http://localhost:5000/api/health || exit 1

USER app

ENTRYPOINT ["dotnet", "Schleusenwerk.dll"]
```

Changes from current:
- Added `curl` installation for HEALTHCHECK.
- Added `EXPOSE 443`.
- Changed `ASPNETCORE_URLS` to bind HTTPS on 443, HTTP on 80, management API on 5000.
- Added `VOLUME ["/data", "/certs"]`.
- Added `HEALTHCHECK` instruction.
- Added `USER app` (non-root).
- Created `/data` dir and set ownership for `app` user.

- [ ] **Step 2: Build locally to verify**

Run from repo root:
```bash
docker build -f src/Schleusenwerk/Dockerfile -t schleusenwerk:test .
```
Expected: Build succeeds, image contains `curl`, runs as non-root user.

- [ ] **Step 3: Verify healthcheck runs**

```bash
docker run -d --name sw-test schleusenwerk:test
docker inspect --format='{{.Config.Healthcheck}}' sw-test
docker rm -f sw-test
```
Expected: Healthcheck config is present with `curl` command.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk/Dockerfile
git commit -m "fix(docker): add HTTPS port, healthcheck, non-root user, volume declarations"
```

---

### Task 2: Improve Health Endpoint

**Files:**
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`

The `/health` endpoint on port 80/443 currently returns a hardcoded 200. The `/api/health` endpoint on port 5000 already queries SQLite + actor system (see `HealthController.cs`). The HEALTHCHECK in the Dockerfile hits `/api/health` which is sufficient. But the simple `/health` should also do a basic liveness check.

- [ ] **Step 1: Update `/health` to verify SQLite is reachable**

In `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`, replace line 11:

```csharp
app.MapGet("/health", () => Results.Ok("healthy"));
```

with:

```csharp
app.MapGet("/health", async (IConfigurationStore store, CancellationToken ct) =>
{
    try
    {
        await store.GetSettingsAsync(ct);
        return Results.Ok("healthy");
    }
    catch
    {
        return Results.StatusCode(503);
    }
});
```

This requires adding the `using Schleusenwerk.Persistence;` import at the top of the file.

- [ ] **Step 2: Verify build compiles**

```bash
dotnet build --configuration Release src/Schleusenwerk.slnx
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs
git commit -m "feat(health): verify SQLite connectivity in liveness endpoint"
```

---

### Task 3: Create Caddyfile for Web Container

**Files:**
- Create: `src/Schleusenwerk.Web/Caddyfile`

- [ ] **Step 1: Create the Caddyfile**

Create `src/Schleusenwerk.Web/Caddyfile`:

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

- [ ] **Step 2: Commit**

```bash
git add src/Schleusenwerk.Web/Caddyfile
git commit -m "feat(web): add Caddyfile for SPA serving and API reverse proxy"
```

---

### Task 4: Create Production Web Dockerfile

**Files:**
- Create: `src/Schleusenwerk.Web/Dockerfile.prod`

- [ ] **Step 1: Create the production Dockerfile**

Create `src/Schleusenwerk.Web/Dockerfile.prod`:

```dockerfile
FROM node:22-alpine AS build

WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM caddy:2-alpine

COPY --from=build /app/dist /srv
COPY Caddyfile /etc/caddy/Caddyfile

EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD wget -qO- http://localhost:3000/health || exit 1
```

Key decisions:
- No `VITE_API_URL` build arg needed — Caddy reverse-proxies `/api/*` and `/hubs/*` to the proxy container, so the SPA makes relative requests.
- `caddy:2-alpine` is ~40MB, supports both amd64 and arm64 natively.
- `wget` is available in alpine base (no extra install needed).

- [ ] **Step 2: Build locally to verify**

Run from `src/Schleusenwerk.Web`:
```bash
docker build -f Dockerfile.prod -t schleusenwerk-web:test .
```
Expected: Build succeeds, produces Caddy image serving Vue SPA.

- [ ] **Step 3: Verify Caddy serves SPA**

```bash
docker run -d --name sw-web-test -p 3001:3000 schleusenwerk-web:test
curl -s http://localhost:3001/ | head -5
curl -s http://localhost:3001/health
docker rm -f sw-web-test
```
Expected: HTML response from `/`, `ok` from `/health`.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.Web/Dockerfile.prod src/Schleusenwerk.Web/Caddyfile
git commit -m "feat(web): add production Dockerfile with Caddy"
```

---

### Task 5: Rewrite Production Docker Compose

**Files:**
- Rewrite: `docker-compose.prod.yml`
- Modify: `docker-compose.yml`

- [ ] **Step 1: Rewrite production compose**

Replace `docker-compose.prod.yml` entirely:

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
      - DOMAINS=${DOMAINS}
      - STAGE=${STAGE:-production}
      - ACME_EMAIL=${ACME_EMAIL}
      - DNS_PROVIDER=${DNS_PROVIDER:-}
    restart: unless-stopped
    networks:
      - schleusenwerk

  web:
    image: ghcr.io/st0o0/schleusenwerk-web:latest
    ports:
      - "3000:3000"
    restart: unless-stopped
    networks:
      - schleusenwerk
    depends_on:
      - proxy

volumes:
  certs:
  data:

networks:
  schleusenwerk:
    driver: bridge
```

Key changes:
- Uses published images from ghcr.io (not local build).
- Port 5000 NOT exposed to host.
- Docker socket read-only (`:ro`).
- Environment variables from `.env` file / shell.
- `restart: unless-stopped` for resilience.
- Removed `version: '3.8'` (deprecated).

- [ ] **Step 2: Update dev compose**

Replace `docker-compose.yml` entirely:

```yaml
services:
  proxy:
    build:
      context: .
      dockerfile: src/Schleusenwerk/Dockerfile
    ports:
      - "1312:80"
      - "1313:443"
      - "5000:5000"
    volumes:
      - ${DOCKER_SOCKET:-/var/run/docker.sock}:/var/run/docker.sock
      - db:/data
      - certs:/certs
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80;https://+:443;http://+:5000
      - Cors__AllowedOrigins=http://localhost:3000
    networks:
      - schleusenwerk

  web:
    build:
      context: src/Schleusenwerk.Web
      dockerfile: Dockerfile.prod
    ports:
      - "3000:3000"
    networks:
      - schleusenwerk
    depends_on:
      - proxy

volumes:
  db:
  certs:

networks:
  schleusenwerk:
    driver: bridge
```

Changes from current:
- Web container uses `Dockerfile.prod` (Caddy) instead of vite preview.
- Proxy `ASPNETCORE_URLS` includes port 443.
- Removed `version: '3.8'`.

- [ ] **Step 3: Commit**

```bash
git add docker-compose.prod.yml docker-compose.yml
git commit -m "feat(docker): rewrite compose files for production and dev"
```

---

### Task 6: Create CI Workflow

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create CI workflow**

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main, mvp]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore src/Schleusenwerk.slnx

      - name: Build
        run: dotnet build --configuration Release --no-restore src/Schleusenwerk.slnx

      - name: Test
        run: dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj --no-build --configuration Release

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '22'
          cache: 'npm'
          cache-dependency-path: src/Schleusenwerk.Web/package-lock.json

      - name: TypeScript Check
        working-directory: src/Schleusenwerk.Web
        run: |
          npm ci
          npx vue-tsc --noEmit
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add build and test workflow"
```

---

### Task 7: Create Docker Build Workflow

**Files:**
- Create: `.github/workflows/docker-build.yml`

- [ ] **Step 1: Create Docker build workflow**

Create `.github/workflows/docker-build.yml`:

```yaml
name: Docker Build

on:
  push:
    branches: [main]
    tags: ['v*']

jobs:
  build-proxy:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Set up QEMU
        uses: docker/setup-qemu-action@v3

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Login to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ghcr.io/st0o0/schleusenwerk
          tags: |
            type=ref,event=branch
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=semver,pattern={{major}}

      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: .
          file: src/Schleusenwerk/Dockerfile
          platforms: linux/amd64,linux/arm64
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

  build-web:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Set up QEMU
        uses: docker/setup-qemu-action@v3

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Login to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ghcr.io/st0o0/schleusenwerk-web
          tags: |
            type=ref,event=branch
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=semver,pattern={{major}}

      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: src/Schleusenwerk.Web
          file: src/Schleusenwerk.Web/Dockerfile.prod
          platforms: linux/amd64,linux/arm64
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/docker-build.yml
git commit -m "ci: add multi-arch Docker build workflow for ghcr.io"
```

---

### Task 8: Write README

**Files:**
- Rewrite: `README.md`

- [ ] **Step 1: Write the README**

Replace `README.md` entirely:

```markdown
# Schleusenwerk

HTTPS reverse proxy with automatic Let's Encrypt certificates and Docker container discovery. Drop-in replacement for [https-portal](https://github.com/SteveLTN/https-portal).

## Quick Start

Create a `docker-compose.yml`:

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
      - DOMAINS=example.com -> http://your-app:8080
      - STAGE=production
      - ACME_EMAIL=admin@example.com
    restart: unless-stopped

  web:
    image: ghcr.io/st0o0/schleusenwerk-web:latest
    ports:
      - "3000:3000"
    restart: unless-stopped

  your-app:
    image: nginx:alpine

volumes:
  certs:
  data:
```

```bash
docker compose up -d
```

The management UI is available at `http://localhost:3000`.

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `DOMAINS` | Yes | — | Route definitions in https-portal format (see below) |
| `STAGE` | No | `production` | `local` (self-signed), `staging` (LE test), `production` (LE live) |
| `ACME_EMAIL` | For production | — | Contact email for Let's Encrypt |
| `DNS_PROVIDER` | No | — | [Lego DNS provider](https://go-acme.github.io/lego/dns/) for DNS-01 challenges / wildcard certs |

### DOMAINS Format

Same format as https-portal:

```bash
# Single domain
DOMAINS=example.com -> http://app:8080

# Multiple domains (comma-separated)
DOMAINS=example.com -> http://app:8080, api.example.com -> http://api:3000

# Redirect
DOMAINS=old.example.com => https://new.example.com
```

## Docker Auto-Discovery

Schleusenwerk watches the Docker socket for containers with these labels:

```yaml
labels:
  schleusenwerk.domain: example.com
  schleusenwerk.upstream: http://container:8080
  schleusenwerk.tls-mode: letsencrypt  # optional: letsencrypt, dns, selfsigned, custom
```

Containers are automatically registered when they start and removed when they stop.

## Volumes

| Path | Purpose |
|------|---------|
| `/data` | SQLite database (proxy configuration and state) |
| `/certs` | TLS certificates (Let's Encrypt, self-signed, custom uploads) |

Both volumes should be persisted to survive container restarts.

## Management UI

The web container (`schleusenwerk-web`) provides a read-only monitoring UI on port 3000. It shows:

- Active routes and their health status
- Upstream servers and load balancing
- Certificate status and expiry
- Docker-discovered containers
- Live event log via WebSocket

> **Note:** Port 5000 (management API) is intentionally not exposed to the host. The web container accesses it internally via the Docker network. Do not expose port 5000 publicly.

## Migrating from https-portal

1. Replace the image name in your `docker-compose.yml`.
2. The `DOMAINS` and `STAGE` environment variables use the same format.
3. Add `ACME_EMAIL` (https-portal infers this, Schleusenwerk requires it explicitly).
4. Mount `/data` and `/certs` volumes for persistence.

## Development

```bash
# Run locally with docker compose
docker compose up -d

# Run the proxy directly
cd src && dotnet run --project Schleusenwerk/Schleusenwerk.csproj

# Run tests
cd src && dotnet test --project Schleusenwerk.Tests/Schleusenwerk.Tests.csproj

# Run the SPA dev server
cd src/Schleusenwerk.Web && npm run dev
```
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add comprehensive README with quick-start and migration guide"
```

---

### Task 9: End-to-End Smoke Test

No new files — verify the full stack works.

- [ ] **Step 1: Build both images locally**

From repo root:
```bash
docker build -f src/Schleusenwerk/Dockerfile -t schleusenwerk:local .
docker build -f src/Schleusenwerk.Web/Dockerfile.prod -t schleusenwerk-web:local src/Schleusenwerk.Web
```
Expected: Both builds succeed.

- [ ] **Step 2: Run with compose**

```bash
docker compose up -d
```
Expected: Both containers start and become healthy.

- [ ] **Step 3: Verify health endpoints**

```bash
curl -s http://localhost:1312/health
curl -s http://localhost:5000/api/health
curl -s http://localhost:3000/health
```
Expected: `healthy` from proxy endpoints, `ok` from web.

- [ ] **Step 4: Verify SPA loads**

```bash
curl -s http://localhost:3000/ | grep -o "SCHLEUSENWERK"
```
Expected: Returns `SCHLEUSENWERK` (from the Vue app HTML).

- [ ] **Step 5: Verify API proxy works through Caddy**

```bash
curl -s http://localhost:3000/api/routes
```
Expected: JSON array (empty or with routes), NOT a 404/502.

- [ ] **Step 6: Cleanup**

```bash
docker compose down -v
```

- [ ] **Step 7: Final commit (if any fixes were needed)**

```bash
git add -A
git commit -m "fix(docker): address issues found during smoke test"
```
