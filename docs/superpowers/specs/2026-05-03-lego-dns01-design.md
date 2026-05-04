# Lego DNS-01 + Environment Config — Design Spec

## Summary

Replace Certes with lego (Go CLI) for all ACME certificate provisioning (HTTP-01 and DNS-01). Add per-domain TLS mode configuration (Docker labels + API). Add STAGE/ACME_EMAIL/DNS_PROVIDER environment variable support at startup. Add Settings REST API for runtime configuration. Enables wildcard certificates and 100+ DNS providers.

## Environment Variables

The proxy reads these environment variables at startup and writes them into `ProxySettings` (SQLite):

| Env Var | ProxySettings Field | Description |
|---------|-------------------|-------------|
| `STAGE` | `Stage` | `local` / `staging` / `production` |
| `ACME_EMAIL` | `AcmeEmail` | Contact email for Let's Encrypt |
| `LEGO_DNS_PROVIDER` | `DnsProvider` | lego provider name (e.g. `cloudflare`, `route53`) |

Env vars override whatever is stored in SQLite at startup. After startup, settings are changeable via the Settings API.

## Settings REST API

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/settings` | Read current ProxySettings |
| PUT | `/api/settings` | Update ProxySettings |

Response/request model:
```
ProxySettingsDto {
  stage: string           // "local" | "staging" | "production"
  acmeEmail: string
  dnsProvider: string     // lego provider name, empty = no DNS-01
  defaultRequestTimeout: number
  maxConnectionsPerUpstream: number
  forceHttpsGlobally: boolean
}
```

Stellwerk page gets editable fields for Stage, ACME Email, DNS Provider with a save button.

## TLS Mode (Per-Domain)

New `TlsMode` enum in `Schleusenwerk.Core`:

| Value | Docker Label | Description |
|-------|-------------|-------------|
| `LetsEncrypt` | `schleusenwerk.tls=letsencrypt` | HTTP-01 via lego (default) |
| `Dns` | `schleusenwerk.tls=dns` | DNS-01 via lego (requires `DnsProvider` in settings) |
| `SelfSigned` | `schleusenwerk.tls=selfsigned` | Self-signed cert |
| `Custom` | `schleusenwerk.tls=custom` | User-uploaded cert |

`DomainConfig` gets a new `TlsMode` property (default: `LetsEncrypt`).

`AddRouteRequest` and `RouteSummary`/`RouteDetail` include `tlsMode` field.

`ContainerLabelParser` maps the label string to the enum.

## Lego Integration

### Binary in Docker Image

```dockerfile
COPY --from=goacme/lego /lego /usr/local/bin/lego
```

### Interface

New `ILegoCertificateProvider` replaces `IAcmeClient`:

```
ProvisionAsync(DomainName domain, TlsMode mode, CancellationToken ct) → X509Certificate2
RenewAsync(DomainName domain, TlsMode mode, CancellationToken ct) → X509Certificate2
```

### CLI Invocations

**HTTP-01:**
```
lego --accept-tos --email={email} --http.webroot=/tmp/acme-webroot
     --path=/certs/lego --domains={domain}
     [--server=staging-url if staging]
     run
```

**DNS-01:**
```
lego --accept-tos --email={email} --dns={provider}
     --path=/certs/lego --domains={domain}
     [--server=staging-url if staging]
     run
```

**Renewal:**
```
lego ... renew --days=30
```

### Webroot Mode

Lego writes HTTP-01 challenge files to `/tmp/acme-webroot/.well-known/acme-challenge/`. The existing ACME challenge endpoint in `SchleusenwerkApplicationSetup` reads files from this directory instead of `AcmeChallengeStore`.

### Lego Data Directory

`/certs/lego/` on the cert volume. Lego stores account keys and certificates there. After lego finishes, the implementation reads the generated cert from lego's output directory and stores it via `ICertificateStore`.

### Staging vs Production

`ProxySettings.Stage` controls the ACME server:
- `Local` → skip lego, use `SelfSignedCertificateGenerator`
- `Staging` → `--server=https://acme-staging-v02.api.letsencrypt.org/directory`
- `Production` → lego default (Let's Encrypt production)

### DNS Provider Environment

Lego reads provider-specific env vars directly from the container environment (e.g. `CF_API_TOKEN` for Cloudflare, `AWS_ACCESS_KEY_ID` for Route53). No abstraction — user follows lego documentation for their provider.

## CertificateProvisioningActor Changes

### Provisioning Flow

1. Receive `CertificateProvisioningRequested`
2. Read `ProxySettings` (Stage, Email, DnsProvider)
3. Read `DomainConfig.TlsMode` for the domain
4. Decision:
   - `Stage == Local` or `TlsMode == SelfSigned` → `SelfSignedCertificateGenerator`
   - `TlsMode == Custom` → skip (user manages cert)
   - `TlsMode == LetsEncrypt` → `ILegoCertificateProvider.ProvisionAsync` with HTTP-01
   - `TlsMode == Dns` → `ILegoCertificateProvider.ProvisionAsync` with DNS-01 (fail if no DnsProvider configured)
5. Store cert via `ICertificateStore`
6. Retry with exponential backoff on failure (1min → 2min → ... → max 1h)

### Auto-Renewal

Unchanged: every 12h, check all certs, renew if < 30 days until expiry. Uses `ILegoCertificateProvider.RenewAsync` instead of `IAcmeClient`.

## What Gets Removed

- `CertesAcmeClient.cs`
- `IAcmeClient.cs`
- `AcmeChallengeStore.cs` (replaced by webroot file reading)
- Certes NuGet package from `Directory.Packages.props` and `Schleusenwerk.csproj`

## What Gets Added

- `ILegoCertificateProvider.cs` — interface
- `LegoCertificateProvider.cs` — implementation (Process.Start lego CLI)
- `SettingsController.cs` — GET/PUT `/api/settings`
- `TlsMode.cs` — enum in Schleusenwerk.Core
- `ProxySettingsDto` in ApiModels
- Settings store: `discovery.ts` → extend, Stellwerk page editable
- Env-var initialization in startup

## Docker Compose Changes

```yaml
proxy:
  environment:
    - STAGE=production
    - ACME_EMAIL=admin@example.com
    - LEGO_DNS_PROVIDER=cloudflare
    - CF_API_TOKEN=xxx  # provider-specific, passed directly to lego
```

## Out of Scope

- Multiple DNS providers per instance (one global provider)
- Per-domain DNS provider override
- ACME EAB (External Account Binding)
- Certificate revocation via lego
