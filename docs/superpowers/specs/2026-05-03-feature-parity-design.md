# Schleusenwerk Feature-Parity — Design Spec

## Summary

Four features to close the gap to https-portal feature parity: Docker Discovery REST endpoint, custom certificate upload, ACME/Let's Encrypt with auto-renewal, and a production docker-compose without exposed management port.

## 1. Discovery REST Endpoint (Hafenbecken)

New `DiscoveryController` in `Schleusenwerk/Controllers/` that queries `DockerDiscoveryActor` via Ask pattern.

### New Command/Response

`DockerDiscoveryActor` receives a new `GetDiscoveredContainers` command and replies with `DiscoveredContainersResult` containing its internal container state.

### Endpoint

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/discovery/containers` | List all discovered Docker containers with labels, assigned domain, conflicts |

### Response Model

```
DiscoveredContainerDto {
  name: string
  image: string
  status: string
  labels: Record<string, string>
  assignedDomain: string | null
  conflictReason: string | null
}
```

### OpenAPI

Add endpoint and schema to `openapi.yaml`.

### Frontend

Replace Hafenbecken placeholder page with real API calls. Display container cards as defined in the original Blueprint design spec: labeled containers with green status, conflict containers with yellow border, unlabeled containers dimmed.

### Pinia Store

New `discovery` store with `fetchContainers()` action wrapping the API call.

## 2. Custom Certificate Upload

Extends existing `CertificateController` with a file upload endpoint. Supports both PEM and PFX formats.

### Endpoint

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/certificates/{domain}/upload` | Upload certificate (PEM or PFX) |

### Request

`multipart/form-data` with:
- `file` — PFX file or PEM cert file (required)
- `keyFile` — PEM key file (required for PEM, not used for PFX)
- `password` — PFX password (optional, only for PFX)

### Format Detection

Backend checks file extension: `.pfx` or `.p12` → PFX flow, everything else → PEM flow.
- **PFX:** `X509Certificate2(bytes, password)` to load
- **PEM:** `X509Certificate2.CreateFromPem(certPem, keyPem)` to combine cert + key

### Validation

- Certificate must not be expired (`NotAfter > DateTime.UtcNow`)
- Domain in certificate (CN or SAN) must match the `{domain}` path parameter
- Private key must match the certificate

On validation failure, return `CommandResult { success: false, errorMessage: "..." }`.

### Storage

Uses existing `ICertificateStore.StoreCertificate()`. SNI selector picks up the new cert automatically on the next TLS handshake.

### Frontend

Siegel page gets an upload button per domain row. Simple file input with optional password field for PFX.

## 3. ACME / Let's Encrypt (HTTP-01)

Extends `CertificateProvisioningActor` to support automatic Let's Encrypt certificate provisioning via HTTP-01 challenge.

### Flow

1. Domain added or renewal triggered
2. Actor checks `ProxySettings.Stage`:
   - `Local` → self-signed (existing behavior, unchanged)
   - `Staging` → Let's Encrypt staging endpoint
   - `Production` → Let's Encrypt production endpoint
3. ACME flow via Certes:
   - Create/load account (persisted as file on cert volume)
   - Create order for domain
   - Retrieve HTTP-01 challenge token
4. Store challenge token in `AcmeChallengeStore` (in-memory singleton)
5. Kestrel port 80 serves `/.well-known/acme-challenge/{token}` — existing HTTPS redirect middleware already exempts this path. New minimal API endpoint reads from `AcmeChallengeStore`
6. Validate challenge with Let's Encrypt, download certificate, store via `ICertificateStore`
7. On failure: retry with exponential backoff (1min → 2min → 4min → max 1h)

### Auto-Renewal

- `CertificateProvisioningActor` receives a periodic `CheckRenewals` tick every 12 hours
- Iterates all certificates in `ICertificateStore`
- If `NotAfter - now < 30 days` → trigger renewal
- If `NotAfter - now < 14 days` → publish `CertificateExpiring` event to EventHub (UI warning in Leitstand + Siegel)

### New Components

| Component | Type | Responsibility |
|-----------|------|----------------|
| `IAcmeClient` | Interface | Abstracts Certes for testability |
| `CertesAcmeClient` | Singleton | Certes implementation: account management, order creation, challenge validation, cert download |
| `AcmeChallengeStore` | Singleton | `ConcurrentDictionary<string, string>` holding pending challenge tokens |
| ACME challenge endpoint | Minimal API | `GET /.well-known/acme-challenge/{token}` reads from `AcmeChallengeStore` |

### Configuration

- `ProxySettings.Stage` (already exists as `AcmeStage` enum: `Local`, `Staging`, `Production`) — controls which ACME endpoint to use
- `ProxySettings.AcmeEmail` (new field) — contact email for Let's Encrypt account registration
- ACME account key persisted as file on the cert volume (alongside certificate files)

### Error Handling

- Challenge validation timeout: 120 seconds, then retry
- Exponential backoff on any ACME failure: 1min → 2min → 4min → 8min → ... → max 1h
- Failed provisioning publishes `CertificateProvisioningFailed` event to EventHub
- Self-signed certificate remains active as fallback until ACME succeeds

## 4. Production Docker Compose

Separate `docker-compose.prod.yml` that does NOT expose port 5000 to the host.

```yaml
services:
  proxy:
    build:
      context: .
      dockerfile: src/Schleusenwerk/Dockerfile
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - db:/data
      - certs:/certs
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    networks:
      - schleusenwerk

  web:
    build:
      context: src/Schleusenwerk.Web
      dockerfile: Dockerfile
    ports:
      - "3000:80"
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

Port 5000 is only reachable within the Docker network. nginx in the web container proxies `/api/*` and `/hubs/*` to `http://proxy:5000` internally. The existing `docker-compose.yml` remains as the dev variant with port 5000 exposed.

## Implementation Order

Technically optimal order:

1. **Discovery endpoint** — small, unblocks Hafenbecken UI, no dependencies
2. **Cert upload** — small, extends existing controller, no dependencies
3. **Production docker-compose** — trivial, can be done anytime
4. **ACME/Let's Encrypt** — largest feature, depends on nothing but benefits from the other features being stable

## Out of Scope

- DNS-01 challenge (only HTTP-01 for now)
- Authentication on the management API (MVP relies on network isolation)
- Wildcard certificate support (requires DNS-01)
- Certificate revocation
- OCSP stapling
