<!-- maggus-id: 8539de72-5608-4497-994a-23387b361b43 -->

# Feature 006: Security Features

## Introduction

Per-Domain Security-Features: Basic Authentication, IP-basierte Zugriffskontrollen (CIDR), HSTS Headers, Security Headers, Server-Token-Hiding und optionales Rate Limiting. Alle Features sind pro Domain konfigurierbar über Config API und Management UI.

### Architecture Context

- **Vision:** Feature-Parität mit https-portal (Basic Auth, IP Restriction) plus Extras (Rate Limiting, Security Headers)
- **Neue Komponenten:** Security Handler Pipeline, BasicAuthHandler, IpRestrictionHandler, SecurityHeadersHandler
- **Akka.NET:** Security-Config ist Teil der Domain-Konfiguration im DomainRouterActor
- **Abhängigkeiten:** Feature 001 (Request Pipeline), Feature 002 (Config Persistence), Feature 004 (UI)
- **Pipeline:** Security Checks werden VOR dem Upstream-Forwarding ausgeführt

## Goals

- Basic Authentication pro Domain mit sicherer Passwort-Speicherung
- IP-basierte Zugriffskontrolle pro Domain (Allow/Deny Listen mit CIDR)
- HSTS Headers pro Domain oder global konfigurierbar
- Security Response Headers (X-Frame-Options, X-Content-Type-Options, etc.)
- Server Token Hiding (Server, X-Powered-By Header entfernen)
- Optionales Rate Limiting pro Domain
- Security Audit Log für alle abgelehnten Requests

## Tasks

### TASK-006-001: Security Configuration Model
**Description:** Als Entwickler möchte ich ein einheitliches Security-Konfigurationsmodell pro Domain, damit alle Security-Features konsistent konfiguriert werden.

**Token Estimate:** ~20k tokens
**Predecessors:** Feature 002 (TASK-002-002)
**Successors:** TASK-006-002, TASK-006-003, TASK-006-004, TASK-006-005
**Parallel:** yes

**Acceptance Criteria:**
- [ ] `SecurityConfig` Record mit BasicAuth, IpRestriction, Hsts, SecurityHeaders, RateLimit Sub-Records
- [ ] Als JSON-Spalte in DomainEntity (EF Core Value Conversion)
- [ ] EF Migration für SecurityConfig Spalte
- [ ] Sinnvolle Defaults (HSTS enabled, Security Headers enabled)
- [ ] Typecheck/lint passes

---

### TASK-006-002: Basic Authentication Handler
**Description:** Als Operator möchte ich bestimmte Domains mit Benutzername/Passwort schützen, damit interne Anwendungen nicht öffentlich zugänglich sind.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-006-001
**Successors:** TASK-006-006
**Parallel:** yes — kann neben TASK-006-003, 004, 005 laufen

**Acceptance Criteria:**
- [ ] HTTP Basic Auth gemäß RFC 7617
- [ ] Passwort-Hashing mit bcrypt (nicht Plaintext)
- [ ] 401 Unauthorized mit WWW-Authenticate Header bei fehlenden/falschen Credentials
- [ ] Mehrere User pro Domain möglich
- [ ] `BCrypt.Net-Next` NuGet Package
- [ ] Security Audit Log bei fehlgeschlagenem Login
- [ ] Unit Tests
- [ ] Typecheck/lint passes

---

### TASK-006-003: IP-Based Access Control
**Description:** Als Operator möchte ich Zugriff auf bestimmte Domains auf bestimmte IP-Bereiche einschränken, damit interne Services nur aus dem Firmennetz erreichbar sind.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-006-001
**Successors:** TASK-006-006
**Parallel:** yes — kann neben TASK-006-002, 004, 005 laufen

**Acceptance Criteria:**
- [ ] Allow-List und Deny-List mit CIDR Notation (z.B. `10.0.0.0/8`, `192.168.0.0/16`)
- [ ] IPv4 und IPv6 Support
- [ ] Client-IP Extraktion: X-Forwarded-For Header oder direkte Connection
- [ ] 403 Forbidden bei blockiertem Zugriff
- [ ] Konfigurierbare Strategie: Allow-First oder Deny-First
- [ ] Security Audit Log bei geblocktem Request
- [ ] Unit Tests für CIDR-Matching
- [ ] Typecheck/lint passes

---

### TASK-006-004: HSTS & Security Response Headers
**Description:** Als Operator möchte ich Security-Headers automatisch zu allen Proxy-Responses hinzufügen, damit Browser-seitige Sicherheit erhöht wird.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-006-001
**Successors:** TASK-006-006
**Parallel:** yes — kann neben TASK-006-002, 003, 005 laufen

**Acceptance Criteria:**
- [ ] HSTS: `Strict-Transport-Security: max-age=X; includeSubDomains; preload`
- [ ] Konfigurierbar: max-age, includeSubDomains, preload (pro Domain + global Default)
- [ ] `X-Frame-Options: DENY` (konfigurierbar: DENY, SAMEORIGIN, ALLOW-FROM)
- [ ] `X-Content-Type-Options: nosniff`
- [ ] `X-XSS-Protection: 0` (modern: deaktiviert, da CSP bevorzugt)
- [ ] `Referrer-Policy: strict-origin-when-cross-origin` (konfigurierbar)
- [ ] Server und X-Powered-By Header entfernen
- [ ] Unit Tests
- [ ] Typecheck/lint passes

---

### TASK-006-005: Rate Limiting
**Description:** Als Operator möchte ich Requests pro Domain rate-limitieren, damit Abuse und DoS-Angriffe abgemildert werden.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-006-001
**Successors:** TASK-006-006
**Parallel:** yes — kann neben TASK-006-002, 003, 004 laufen

**Acceptance Criteria:**
- [ ] Token-Bucket Algorithmus pro Domain
- [ ] Konfigurierbar: Requests/Sekunde, Burst-Größe
- [ ] Per-IP Rate Limiting (optional, zusätzlich zu per-Domain)
- [ ] 429 Too Many Requests mit Retry-After Header
- [ ] In-Memory State (kein Redis nötig)
- [ ] Security Audit Log bei Rate Limit Überschreitung
- [ ] Unit Tests für Token-Bucket Logik
- [ ] Typecheck/lint passes

---

### TASK-006-006: Security Pipeline Integration
**Description:** Als Entwickler möchte ich alle Security-Handler in die Request-Pipeline integrieren, damit Security-Checks vor dem Upstream-Forwarding ausgeführt werden.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-006-002, TASK-006-003, TASK-006-004, TASK-006-005
**Successors:** TASK-006-007
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] Security Handler Pipeline: IP Check → Rate Limit → Basic Auth → (Proxy) → Security Headers
- [ ] Pipeline-Reihenfolge ist korrekt (billigste Checks zuerst)
- [ ] Handler werden nur aktiviert wenn für die Domain konfiguriert
- [ ] Lazy Loading der Security-Config pro Domain
- [ ] Integration in ProxyRequestHandler (Feature 001)
- [ ] Typecheck/lint passes

---

### TASK-006-007: Security Audit Service & Log
**Description:** Als Security-Operator möchte ich alle Security-Events (Auth-Fehler, IP-Blocks, Rate Limits) auditieren, damit ich Sicherheitsvorfälle untersuchen kann.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-006-006
**Successors:** TASK-006-008
**Parallel:** no

**Acceptance Criteria:**
- [ ] `SecurityAuditService` loggt alle Security-Denials
- [ ] Felder: Timestamp, Client-IP, Domain, Event-Type, Details
- [ ] Speicherung in SQLite (separate Tabelle)
- [ ] Konfigurierbare Retention (default: 90 Tage, Auto-Cleanup)
- [ ] API Endpoint: `GET /api/security/audit` mit Filtern
- [ ] Typecheck/lint passes

---

### TASK-006-008: Integration Tests
**Description:** Als Entwickler möchte ich End-to-End Tests für alle Security-Features, damit ich sicher bin dass der Schutz funktioniert.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-006-006, TASK-006-007
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Test: Basic Auth mit korrekten/falschen Credentials
- [ ] Test: IP Allow-List blockiert Requests von außerhalb
- [ ] Test: IP Deny-List blockiert spezifische IPs
- [ ] Test: HSTS Header in Response vorhanden
- [ ] Test: Security Headers in Response vorhanden
- [ ] Test: Server/X-Powered-By Header entfernt
- [ ] Test: Rate Limit gibt 429 zurück
- [ ] Test: Security Audit Log wird geschrieben
- [ ] Alle Tests grün
- [ ] Typecheck/lint passes

## Task Dependency Graph

```
TASK-006-001 ──→ TASK-006-002 ──→ TASK-006-006 ──→ TASK-006-007 ──→ TASK-006-008
             ├─→ TASK-006-003 ──┘
             ├─→ TASK-006-004 ──┘
             └─→ TASK-006-005 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-006-001 | ~20k | Feature 002 | yes | — |
| TASK-006-002 | ~35k | 001 | yes (mit 003, 004, 005) | — |
| TASK-006-003 | ~30k | 001 | yes (mit 002, 004, 005) | — |
| TASK-006-004 | ~25k | 001 | yes (mit 002, 003, 005) | — |
| TASK-006-005 | ~35k | 001 | yes (mit 002, 003, 004) | — |
| TASK-006-006 | ~30k | 002, 003, 004, 005 | no | opus |
| TASK-006-007 | ~30k | 006 | no | — |
| TASK-006-008 | ~40k | 006, 007 | no | — |

**Total estimated tokens:** ~245k

## Functional Requirements

- FR-1: Basic Auth muss HTTP 401 mit WWW-Authenticate Header bei fehlenden Credentials zurückgeben
- FR-2: Passwörter müssen mit bcrypt gehasht gespeichert werden (niemals Plaintext)
- FR-3: IP-Restriction muss CIDR Notation für IPv4 und IPv6 unterstützen
- FR-4: HSTS max-age muss pro Domain konfigurierbar sein
- FR-5: Security Headers müssen bei jeder Response gesetzt werden
- FR-6: Rate Limiting muss 429 mit Retry-After Header zurückgeben
- FR-7: Alle Security-Denials müssen im Audit Log mit Client-IP und Grund erscheinen

## Non-Goals (Out of Scope)

- Kein OAuth2 / OpenID Connect Support
- Kein WAF (Web Application Firewall) mit Regelwerk
- Keine Content Security Policy (CSP) Management
- Kein mTLS / Client-Zertifikat Validierung
- Keine Geo-IP Blocking

## Technical Considerations

- Security Pipeline Reihenfolge: IP Check (billigste) → Rate Limit → Basic Auth → Proxy → Security Headers
- bcrypt für Passwort-Hashing — BCrypt.Net-Next Package
- CIDR-Matching via System.Net.IPAddress und IPNetwork
- Token-Bucket in-memory — bei Multi-Instance Deployment nicht synchronisiert (Hinweis in Docs)
- Security Audit Log in SQLite — separate Tabelle mit Auto-Cleanup Hintergrundtask
- X-Forwarded-For für echte Client-IP — Trust-Konfiguration für Proxy-Chains nötig

## Success Metrics

- Basic Auth blockiert Requests ohne Credentials zuverlässig
- IP-Restriction blockiert Requests von nicht-erlaubten IPs
- Security Headers sind in jeder Proxy-Response vorhanden
- Rate Limiting begrenzt Requests korrekt auf konfigurierte Rate
- Audit Log enthält alle Security-Events mit Details

## Open Questions

*Keine offenen Fragen.*
