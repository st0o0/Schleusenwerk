<!-- maggus-id: 2790a06f-5df3-4501-8410-3318ac762ad8 -->

# Feature 003: TLS Termination & ACME Certificate Management

## Introduction

Kestrel-based TLS termination with SNI (Server Name Indication) for multi-domain HTTPS. Automatic certificate management via Let's Encrypt ACME v2 (HTTP-01 challenge) with three modes: self-signed (local), staging, and production. Automatic renewal and rate limit awareness. Certificates are persisted via Akka.Persistence.

### Architecture Context

- **Vision:** Automatic HTTPS certificates like https-portal, but natively in .NET
- **New Components:** CertificateRenewalActor, AcmeClientService, SniCertificateSelector, SelfSignedGenerator
- **Akka.NET:** CertificateRenewalActor monitors expiry dates and triggers renewal; certificate state persisted via Akka.Persistence
- **Dependencies:** Feature 002 (Akka.Persistence setup), Feature 001 (Kestrel listener)

## Goals

- Multi-domain TLS termination with SNI-based certificate selection
- Automatic Let's Encrypt certificates via ACME v2 HTTP-01 challenge
- Three modes: local (self-signed), staging (LE staging), production (LE production)
- Automatic renewal with configurable lead time (default: 30 days)
- Certificate storage via Akka.Persistence
- RSA and ECC key support
- Rate limit awareness with backoff and self-signed fallback

## Tasks

### TASK-003-001: ACME NuGet Package & Certificate Models
**Description:** As a developer I want an ACME v2 client and certificate models in the project so I can build the ACME integration.

**Token Estimate:** ~20k tokens
**Predecessors:** Feature 002 (TASK-002-002)
**Successors:** TASK-003-002, TASK-003-003
**Parallel:** yes
**Model:** haiku

**Acceptance Criteria:**
- [ ] `Certes` NuGet package in Directory.Packages.props
- [ ] Certificate domain model (Domain, PublicKey, PrivateKey, Issuer, IssuedDate, ExpiryDate, KeyType) as immutable record
- [ ] Certificate state integrated into Akka.Persistence actor model
- [ ] Typecheck/lint passes

---

### TASK-003-002: Self-Signed Certificate Generator
**Description:** As an operator I want to generate self-signed certificates in local mode so that development and testing work without Let's Encrypt.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-003-001
**Successors:** TASK-003-005
**Parallel:** yes — can run alongside TASK-003-003

**Acceptance Criteria:**
- [ ] Generates X.509 self-signed cert for any domain
- [ ] Supports RSA (2048/4096) and ECC (P-256)
- [ ] Configurable validity period (default: 365 days)
- [ ] Cert is persisted via Akka.Persistence (PFX bytes stored as event)
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-003-003: ACME v2 Client Service
**Description:** As a reverse proxy I want to automatically request Let's Encrypt certificates via ACME v2 so that domains become HTTPS-capable without manual intervention.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-003-001
**Successors:** TASK-003-004, TASK-003-005
**Parallel:** yes — can run alongside TASK-003-002
**Model:** opus

**Acceptance Criteria:**
- [ ] Wrapper around Certes library
- [ ] Account registration with Let's Encrypt (email configurable)
- [ ] Certificate order for individual domains
- [ ] HTTP-01 challenge-response flow
- [ ] Staging and production URL configurable
- [ ] Certificate persisted via Akka.Persistence
- [ ] Error handling with descriptive error messages
- [ ] Unit tests with mocked ACME server
- [ ] Typecheck/lint passes

---

### TASK-003-004: HTTP-01 Challenge Handler
**Description:** As an ACME client I want to respond to HTTP-01 challenges so that Let's Encrypt can verify domain ownership.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-003-003
**Successors:** TASK-003-005
**Parallel:** no

**Acceptance Criteria:**
- [ ] Kestrel endpoint for `/.well-known/acme-challenge/{token}`
- [ ] Token to key-authorization mapping in-memory during challenge
- [ ] Endpoint is only active during active ACME orders
- [ ] Port 80 must be reachable (hint in logs if not)
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-003-005: Certificate Management Service
**Description:** As a system I want a central service layer for certificate management so that all certificate operations (provisioning, storage, retrieval, renewal) are consolidated.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-003-002, TASK-003-003, TASK-003-004
**Successors:** TASK-003-006, TASK-003-007, TASK-003-008
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `ICertificateService` interface
- [ ] `ProvisionCertificate(domain, mode)` — self-signed or ACME depending on mode
- [ ] `GetCertificate(domain)` — load certificate from persisted state
- [ ] `RenewCertificate(domain)` — trigger renewal
- [ ] `GetAllCertificates()` — overview of all certificates with status
- [ ] Mode selection: local -> self-signed, staging -> LE staging, production -> LE production
- [ ] Publishes `CertificateProvisioned`, `CertificateRenewed` events
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-003-006: Certificate Renewal Actor
**Description:** As an operator I want certificates to be automatically renewed before expiry so that no manual intervention is needed.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-003-005
**Successors:** TASK-003-009
**Parallel:** yes — can run alongside TASK-003-007
**Model:** opus

**Acceptance Criteria:**
- [ ] `CertificateRenewalActor` periodically checks all certificates (default: daily)
- [ ] Renews when expiry < configurable margin days (default: 30)
- [ ] Publishes `CertificateExpiringSoon` warning at < 14 days
- [ ] Uses CertificateService for actual renewal
- [ ] Logging of all renewal activities
- [ ] Unit tests with Akka.TestKit and mocked time
- [ ] Typecheck/lint passes

---

### TASK-003-007: SNI Certificate Selector
**Description:** As a Kestrel server I want to select the correct certificate for the requested domain during TLS handshake so that multi-domain HTTPS works.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-003-005
**Successors:** TASK-003-008
**Parallel:** yes — can run alongside TASK-003-006

**Acceptance Criteria:**
- [ ] Implements Kestrel `ServerCertificateSelector` callback
- [ ] In-memory cache for loaded X509Certificate2 objects
- [ ] Cache invalidation on certificate updates (event-based)
- [ ] Fallback to default certificate when domain is unknown
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-003-008: Kestrel HTTPS & TLS Configuration
**Description:** As a developer I want to configure Kestrel with TLS and SNI so that HTTPS on port 443 works with multi-domain support.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-003-007
**Successors:** TASK-003-009
**Parallel:** no

**Acceptance Criteria:**
- [ ] Kestrel listens on port 443 (HTTPS) and port 80 (HTTP)
- [ ] TLS 1.2 and 1.3 enabled
- [ ] ServerCertificateSelector registered
- [ ] HTTP port 80 -> HTTPS redirect (or ACME challenge)
- [ ] Port configuration via appsettings.json / env vars
- [ ] Typecheck/lint passes

---

### TASK-003-009: Rate Limit Awareness & Fallback
**Description:** As a system I want to detect Let's Encrypt rate limits and respond with backoff so the proxy doesn't fail on ACME problems.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-003-005, TASK-003-006
**Successors:** TASK-003-010
**Parallel:** no

**Acceptance Criteria:**
- [ ] Detects 429 responses and ACME-specific rate limit errors
- [ ] Exponential backoff on rate limits (1h -> 2h -> 4h -> max 24h)
- [ ] Fallback to self-signed when ACME fails
- [ ] Warning in logs and events when in fallback mode
- [ ] Retry after backoff period
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-003-010: Certificate API Endpoints
**Description:** As a UI developer I want REST endpoints for certificate management so the management UI can display certificates and manually trigger renewal.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-003-005, TASK-003-009
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] `GET /api/certificates` — all certificates with status
- [ ] `GET /api/certificates/{domain}` — certificate details
- [ ] `POST /api/certificates/{domain}/renew` — trigger manual renewal
- [ ] `GET /api/certificates/{domain}/status` — renewal status
- [ ] Minimal API endpoints
- [ ] Typecheck/lint passes

---

### TASK-003-011: Integration Tests
**Description:** As a developer I want end-to-end tests for TLS and certificate management so I can be confident that HTTPS works correctly.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-003-008, TASK-003-009, TASK-003-010
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Test: Self-signed cert generation and Kestrel TLS handshake
- [ ] Test: SNI selects correct certificate per domain
- [ ] Test: ACME challenge handler responds correctly
- [ ] Test: Certificate renewal actor triggers on expiry
- [ ] Test: Rate limit fallback to self-signed
- [ ] Test: Certificate persisted and recovered via Akka.Persistence
- [ ] All tests green
- [ ] Typecheck/lint passes

## Task Dependency Graph

```
TASK-003-001 --> TASK-003-002 --> TASK-003-005 --> TASK-003-006 --> TASK-003-009 --> TASK-003-010 --> TASK-003-011
             \-> TASK-003-003 --> TASK-003-004 --/  \-> TASK-003-007 --> TASK-003-008 --------------/
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-003-001 | ~20k | Feature 002 | yes | haiku |
| TASK-003-002 | ~25k | 001 | yes (with 003) | — |
| TASK-003-003 | ~75k | 001 | yes (with 002) | opus |
| TASK-003-004 | ~25k | 003 | no | — |
| TASK-003-005 | ~50k | 002, 003, 004 | no | opus |
| TASK-003-006 | ~40k | 005 | yes (with 007) | opus |
| TASK-003-007 | ~30k | 005 | yes (with 006) | — |
| TASK-003-008 | ~25k | 007 | no | — |
| TASK-003-009 | ~30k | 005, 006 | no | — |
| TASK-003-010 | ~25k | 005, 009 | no | — |
| TASK-003-011 | ~50k | 008, 009, 010 | no | — |

**Total estimated tokens:** ~395k

## Functional Requirements

- FR-1: Kestrel must support TLS with SNI-based certificate selection for any number of domains
- FR-2: In local mode, self-signed certificates must be automatically generated
- FR-3: In staging/production mode, Let's Encrypt certificates must be obtained via ACME v2 HTTP-01 challenge
- FR-4: Certificates must be automatically renewed before expiry (configurable margin days)
- FR-5: On ACME errors or rate limits, a self-signed fallback must be activated
- FR-6: Certificates must be persisted via Akka.Persistence with metadata (domain, issuer, expiry, key type)
- FR-7: Port 80 must remain reachable for ACME HTTP-01 challenges

## Non-Goals (Out of Scope)

- No DNS-01 challenge support (HTTP-01 only)
- No wildcard certificates (require DNS-01)
- No multi-domain SAN certificate (one cert per domain)
- No mTLS / client certificate validation
- No OCSP stapling in V1

## Technical Considerations

- Certes as ACME v2 client library (actively maintained, .NET native)
- Kestrel ServerCertificateSelector callback for SNI
- X509Certificate2 objects in memory cache (not loaded from persistence for every connection)
- Private keys stored as encrypted events via Akka.Persistence
- ACME account key also persisted via Akka.Persistence
- Let's Encrypt rate limits: 50 certs/domain/week, 5 duplicates/week, 5 failed validations/hour

## Success Metrics

- HTTPS works for all configured domains with the correct certificate
- Self-signed mode works without external dependencies
- Automatic renewal triggers reliably before expiry
- Rate limit fallback keeps the proxy running

## Open Questions

*No open questions.*
