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
