<!-- maggus-id: f66c2387-856d-414f-9147-73ed5c27d41c -->

# Feature 004: Management Web UI

## Introduction

Embedded Blazor Server web UI for managing the reverse proxy. Dashboard with domain overview, certificate status, and health monitoring. Domain management, certificate viewer, logs, and global settings — all in the same ASP.NET Core app.

### Architecture Context

- **Vision:** "a bit nicer and with a small UI" — the main differentiator from https-portal
- **Technology:** Blazor Server — runs in the same process, no separate build, SignalR for real-time updates
- **New Components:** Blazor components, DashboardHub (SignalR), ManagementEndpoints
- **Akka.NET:** Events from actors are pushed to the UI via SignalR — no polling
- **Dependencies:** Features 001, 002, 003 must provide the API endpoints

## Goals

- Dashboard with real-time overview of all domains, certificates, and proxy health
- Domain management: add, edit, delete domains with live reload
- Certificate viewer: expiry dates, status, manual renewal
- Activity/error logs with filters
- Global settings (ACME mode, redirect code, TLS version, etc.)
- Password-protected access to the management UI

## Tasks

### TASK-004-001: Blazor Server Infrastructure
**Description:** As a developer I want the Blazor Server infrastructure set up so I can build UI components.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-004-002, TASK-004-003, TASK-004-004, TASK-004-005, TASK-004-006, TASK-004-007
**Parallel:** yes

**Acceptance Criteria:**
- [ ] Blazor Server configured in Program.cs (AddRazorComponents, MapRazorComponents)
- [ ] App.razor, _Routes.razor created
- [ ] MainLayout.razor with sidebar navigation (Dashboard, Domains, Certificates, Logs, Settings)
- [ ] CSS framework integrated (Bootstrap 5 via CDN or LibMan)
- [ ] Management UI under `/manage` path (not root — root is the proxy)
- [ ] `dotnet build` succeeds
- [ ] Typecheck/lint passes

---

### TASK-004-002: SignalR Hub & Akka.NET Event Bridge
**Description:** As a UI developer I want real-time updates from Akka.NET actors in the UI so the dashboard updates live.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-003
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `DashboardHub` SignalR hub
- [ ] Event bridge: Akka.NET EventStream -> SignalR groups
- [ ] Events: DomainAdded/Removed, CertificateRenewed, UpstreamHealthChanged
- [ ] Clients subscribe per domain or globally
- [ ] Registered in Program.cs
- [ ] Typecheck/lint passes

---

### TASK-004-003: Dashboard Page
**Description:** As an operator I want a dashboard with an overview of all domains, certificate status, and health so I can see the proxy state at a glance.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-002
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-004, 005, 006

**Acceptance Criteria:**
- [ ] System health badge (Healthy/Degraded/Unhealthy)
- [ ] Count of active domains, valid certificates
- [ ] Domain cards with status, upstream count, cert expiry
- [ ] Color coding: green (OK), yellow (cert expiring soon), red (error)
- [ ] Real-time updates via SignalR
- [ ] **Verify in browser using dev server**
- [ ] Typecheck/lint passes

---

### TASK-004-004: Domain Management Page
**Description:** As an operator I want to add, edit, and delete domains via the UI so I can configure the proxy.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-004-001
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-003, 005, 006

**Acceptance Criteria:**
- [ ] Domain list with table (domain, upstreams, status, actions)
- [ ] "Add Domain" form: domain name, upstream(s), redirect mode, ACME mode
- [ ] "Edit Domain" form with pre-filled values
- [ ] "Delete Domain" with confirmation dialog
- [ ] Client-side validation (domain format, URL format)
- [ ] Calls Feature 002 API endpoints
- [ ] Live reload: changes visible immediately
- [ ] **Verify in browser using dev server**
- [ ] Typecheck/lint passes

---

### TASK-004-005: Certificate Viewer Page
**Description:** As an operator I want to see all certificates with details and renewal status and manually trigger renewal.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-004-001
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-003, 004, 006

**Acceptance Criteria:**
- [ ] Certificate list: domain, issuer, expiry date, type (self-signed/LE), status
- [ ] Status badges: Valid, Expiring Soon (<30d), Expired, Renewing
- [ ] Detail view: fingerprint, key type, issued date, chain info
- [ ] "Renew Now" button with confirmation
- [ ] Calls Feature 003 API endpoints
- [ ] Real-time updates on renewal
- [ ] **Verify in browser using dev server**
- [ ] Typecheck/lint passes

---

### TASK-004-006: Logs Viewer Page
**Description:** As an operator I want to see proxy logs and errors in the UI so I can debug problems.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-002
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-003, 004, 005

**Acceptance Criteria:**
- [ ] Log list with level (Info, Warning, Error), timestamp, domain, message
- [ ] Filter by level, domain, time range
- [ ] Live streaming of new logs via SignalR
- [ ] Pause/resume streaming
- [ ] In-memory ring buffer for logs (configurable size, default: 10,000 entries)
- [ ] **Verify in browser using dev server**
- [ ] Typecheck/lint passes

---

### TASK-004-007: Settings Page
**Description:** As an operator I want to configure global settings so system-wide behavior is customizable.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-004-001
**Successors:** none
**Parallel:** yes — can run alongside TASK-004-003, 004, 005, 006

**Acceptance Criteria:**
- [ ] ACME mode selection (Local/Staging/Production)
- [ ] ACME email address
- [ ] Default HTTP-to-HTTPS redirect code (301/307)
- [ ] TLS minimum version (1.2/1.3)
- [ ] Certificate renewal margin days
- [ ] Log level
- [ ] Docker discovery enable/disable
- [ ] Changes effective immediately (hot-reload)
- [ ] **Verify in browser using dev server**
- [ ] Typecheck/lint passes

---

### TASK-004-008: Management UI Authentication
**Description:** As an operator I want the management UI to be password-protected so unauthorized users cannot access it.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-004-001
**Successors:** none
**Parallel:** yes — can run alongside other UI tasks

**Acceptance Criteria:**
- [ ] Login page with username/password
- [ ] Session-based authentication (cookie)
- [ ] Admin credentials configurable via env vars or settings
- [ ] Default password on first start with hint to change
- [ ] Logout button in MainLayout
- [ ] All `/manage/*` routes protected
- [ ] Typecheck/lint passes

## Task Dependency Graph

```
TASK-004-001 --> TASK-004-002 --> TASK-004-003
             |                \-> TASK-004-006
             |--> TASK-004-004
             |--> TASK-004-005
             |--> TASK-004-007
             \--> TASK-004-008
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-004-001 | ~30k | none | yes | — |
| TASK-004-002 | ~35k | 001 | no | opus |
| TASK-004-003 | ~40k | 002 | yes (with 004-008) | — |
| TASK-004-004 | ~50k | 001 | yes (with 003, 005-008) | — |
| TASK-004-005 | ~35k | 001 | yes (with 003, 004, 006-008) | — |
| TASK-004-006 | ~40k | 002 | yes (with 003-005, 007-008) | — |
| TASK-004-007 | ~30k | 001 | yes (with 003-006, 008) | — |
| TASK-004-008 | ~35k | 001 | yes (with 003-007) | — |

**Total estimated tokens:** ~295k

## Functional Requirements

- FR-1: Dashboard must display real-time status of all domains, certificates, and upstreams
- FR-2: Domains must be creatable, editable, and deletable via the UI
- FR-3: Changes via the UI must take effect immediately (no restart)
- FR-4: Certificates must be displayed with expiry date and status
- FR-5: Manual certificate renewal must be possible via the UI
- FR-6: Logs must be live-streamed and filterable by level/domain/time
- FR-7: Management UI must be password-protected

## Non-Goals (Out of Scope)

- No multi-user system (one admin account is sufficient)
- No mobile-optimized UI (desktop-first, responsive is nice-to-have)
- No dark mode in V1
- No i18n / multi-language support (English only)
- No metrics/Grafana integration

## Design Considerations

### UI Mockup — Dashboard

```
+-----------------------------------------------------------+
|  Schleusenwerk                        [Admin v] [Logout]  |
+----------------+------------------------------------------+
| Dashboard      |  System Health: * HEALTHY                |
| Domains        |  Domains: 12  |  Certificates: 12       |
| Certificates   |                                          |
| Logs           |  +------------+--------+-----+--------+  |
| Settings       |  | Domain     | Status | Exp | Upstr. |  |
|                |  +------------+--------+-----+--------+  |
|                |  | api.ex.com | * OK   | 67d | 2/2    |  |
|                |  | web.ex.com | * OK   | 45d | 1/1    |  |
|                |  | old.ex.com | * EXP  | -2d | 1/1    |  |
|                |  | new.ex.com | ~ REN  | 15d | 3/3    |  |
|                |  +------------+--------+-----+--------+  |
|                |  [+ Add Domain]                          |
+----------------+------------------------------------------+
```

## Technical Considerations

- Blazor Server runs in the same process — no CORS, no separate port
- Management UI under `/manage` path to not collide with proxy traffic
- SignalR connection for live updates — automatic reconnect
- In-memory log ring buffer to avoid database load
- Bootstrap 5 for quick, clean styling

## Success Metrics

- Dashboard shows correct status of all domains and certificates
- Domain CRUD via UI works and takes effect immediately
- Logs are live-streamed without delay
- Login protects all management pages

## Open Questions

*No open questions.*
