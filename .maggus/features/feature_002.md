<!-- maggus-id: 9c0be7ae-1e0d-47af-ba83-adb151f6f292 -->

# Feature 002: Configuration & Persistence System

## Introduction

Akka.Persistence-based persistence system for domain configurations, certificates, and settings. JSON config file support with import/export, hot-reload without restart, and environment variable overrides for Docker deployments. Uses Akka.NET's RoundRobinRouter for distributing configuration update propagation.

### Architecture Context

- **Vision:** Central configuration layer used by UI (Feature 004), Docker Discovery (Feature 005), and Security (Feature 006)
- **New Components:** ConfigurationPersistenceActor, ConfigurationUpdateRouter (RoundRobinRouter), JSON Import/Export, Env Var Provider
- **Akka.NET:** ConfigurationPersistenceActor uses event-sourced persistence; ConfigurationUpdateRouter (RoundRobinPool) distributes change propagation to subscriber actors
- **Foundation:** This feature must be stable before Features 001, 003, 004

## Goals

- Persist domain configurations, certificates, and settings via Akka.Persistence
- JSON config file import/export for backup and version control
- Hot-reload: apply configuration changes without restart
- Environment variables as override for Docker deployments
- Validate all configuration changes before applying

## Tasks

### TASK-002-001: NuGet Packages & Akka.Persistence Setup
**Description:** As a developer I want Akka.Persistence with a suitable journal/snapshot store in the project so I can build the persistence layer.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-002-002
**Parallel:** yes — can run alongside Feature 001 tasks
**Model:** haiku

**Acceptance Criteria:**
- [ ] `Akka.Persistence` NuGet package in Directory.Packages.props
- [ ] Suitable journal/snapshot store package (e.g. `Akka.Persistence.Sqlite` or `Akka.Persistence.Sql`) in Directory.Packages.props
- [ ] PackageReferences in Schleusenwerk.csproj
- [ ] Akka.Persistence configured via Akka.Hosting in Program.cs
- [ ] `dotnet build` succeeds
- [ ] Typecheck/lint passes

---

### TASK-002-002: Persistent Configuration Actor & Domain Models
**Description:** As a developer I want a persistent actor with event-sourced state for domains, upstreams, certificates, and settings so that all configuration data survives restarts.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-002-001
**Successors:** TASK-002-003, TASK-002-004
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `ConfigurationPersistenceActor` as `ReceivePersistentActor` with event-sourced state
- [ ] Commands: `AddDomain`, `UpdateDomain`, `RemoveDomain`, `AddUpstream`, `RemoveUpstream`, `UpdateSettings`
- [ ] Events: `DomainAdded`, `DomainRemoved`, `DomainUpdated`, `UpstreamAdded`, `UpstreamRemoved`, `SettingsUpdated`
- [ ] State rebuilt from journal on recovery
- [ ] Snapshot support for faster recovery (configurable interval)
- [ ] Domain models: DomainConfig, UpstreamTarget, CertificateInfo, ProxySettings as immutable records
- [ ] Typecheck/lint passes

---

### TASK-002-003: State Recovery & Snapshot Strategy
**Description:** As a developer I want reliable state recovery from journal and snapshots so that configuration persists across restarts with fast startup.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-002-002
**Successors:** TASK-002-004
**Parallel:** no

**Acceptance Criteria:**
- [ ] Snapshot saved every N events (configurable, default: 100)
- [ ] Recovery from latest snapshot + replay of subsequent events
- [ ] Stale snapshot cleanup (keep last N snapshots)
- [ ] Unit tests for recovery scenarios (empty journal, snapshot + events, snapshot only)
- [ ] Typecheck/lint passes

---

### TASK-002-004: Configuration Service (Query & Command API)
**Description:** As a developer I want a service layer with query and command methods for all configuration data so that business logic doesn't interact directly with actors.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-002-002, TASK-002-003
**Successors:** TASK-002-005
**Parallel:** no

**Acceptance Criteria:**
- [ ] `IConfigurationService` interface
- [ ] Commands via Ask pattern to ConfigurationPersistenceActor: Add, Update, Remove domains/upstreams
- [ ] Queries: GetAll, GetByDomain (read from actor state via Ask)
- [ ] Settings queries: GetSetting, UpdateSetting
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-002-005: Configuration Service with Validation
**Description:** As an operator I want configuration changes to be validated before they are applied so that invalid configs don't break the proxy.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-002-004
**Successors:** TASK-002-006, TASK-002-007, TASK-002-009
**Parallel:** no

**Acceptance Criteria:**
- [ ] Validation: domain format, upstream URL format, no duplicates
- [ ] Business logic: adding a domain triggers certificate provisioning (event)
- [ ] Publishes `DomainAdded`, `DomainRemoved`, `DomainUpdated` events via EventStream
- [ ] Error handling with Result pattern (no exception-based flow)
- [ ] Unit tests for validation and events
- [ ] Typecheck/lint passes

---

### TASK-002-006: Configuration Update Router (RoundRobinRouter)
**Description:** As an Akka.NET system I want a RoundRobinRouter that distributes configuration change propagation to subscriber actors so that updates are processed efficiently and in real-time.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-002-005
**Successors:** TASK-002-010
**Parallel:** yes — can run alongside TASK-002-007, TASK-002-008
**Model:** opus

**Acceptance Criteria:**
- [ ] `ConfigurationUpdateRouter` using Akka.NET `RoundRobinPool` or `RoundRobinGroup`
- [ ] Subscribes to EventStream for configuration change events
- [ ] Propagates `UpdateRoutes` to DomainRouterActor (Feature 001)
- [ ] Propagates `ProvisionCertificate` to CertificateActor (Feature 003)
- [ ] Dynamic routee management (add/remove subscribers at runtime)
- [ ] Logging of all configuration changes
- [ ] Unit tests with Akka.TestKit
- [ ] Typecheck/lint passes

---

### TASK-002-007: JSON Config Import
**Description:** As an operator I want to import domain configurations from a JSON file so I can version configs and share them between instances.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-002-005
**Successors:** TASK-002-009
**Parallel:** yes — can run alongside TASK-002-006 and TASK-002-008

**Acceptance Criteria:**
- [ ] JSON schema for config file defined
- [ ] Parser reads JSON and maps to domain commands
- [ ] Validation same as normal config update
- [ ] Merge strategy: existing domains are updated, new ones are added
- [ ] Import on app start when config file is present
- [ ] Unit tests for JSON parsing and merge logic
- [ ] Typecheck/lint passes

---

### TASK-002-008: JSON Config Export
**Description:** As an operator I want to export the current configuration as JSON so I can create backups and migrate configs.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-002-005
**Successors:** TASK-002-009
**Parallel:** yes — can run alongside TASK-002-006 and TASK-002-007

**Acceptance Criteria:**
- [ ] Export all domains, upstreams, and settings as JSON
- [ ] Same schema as import (roundtrip-capable)
- [ ] Sensitive data (certificate private keys) optionally excluded
- [ ] Unit tests for export and roundtrip
- [ ] Typecheck/lint passes

---

### TASK-002-009: Configuration API Endpoints
**Description:** As a UI developer I want REST endpoints for configuration changes so the management UI can manage domains.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-002-005, TASK-002-007, TASK-002-008
**Successors:** TASK-002-010
**Parallel:** no

**Acceptance Criteria:**
- [ ] `GET /api/config/domains` — all domains
- [ ] `GET /api/config/domains/{domain}` — single domain
- [ ] `POST /api/config/domains` — add domain
- [ ] `PUT /api/config/domains/{domain}` — update domain
- [ ] `DELETE /api/config/domains/{domain}` — remove domain
- [ ] `POST /api/config/validate` — validate config without applying
- [ ] `GET /api/config/export` — JSON export
- [ ] `POST /api/config/import` — JSON import
- [ ] Minimal API endpoints (no controllers)
- [ ] Typecheck/lint passes

---

### TASK-002-010: Environment Variable Provider
**Description:** As a Docker operator I want to configure domains and upstreams via environment variables so containers can be deployed without config files.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-002-005
**Successors:** none
**Parallel:** yes — can run alongside TASK-002-009

**Acceptance Criteria:**
- [ ] Reads `DOMAINS` env var in https-portal format: `example.com -> http://upstream:80`
- [ ] Supports comma-separated multi-domain syntax
- [ ] Supports redirect syntax: `www.example.com => https://example.com`
- [ ] Env vars have higher priority than JSON/persisted config
- [ ] `STAGE` env var for ACME mode (local/staging/production)
- [ ] Unit tests for env var parsing
- [ ] Typecheck/lint passes

---

### TASK-002-011: Bootstrap & Integration in Program.cs
**Description:** As a developer I want the configuration to be correctly loaded at app start (Akka.Persistence recovery -> JSON -> env vars) so the proxy is immediately operational.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-002-006, TASK-002-009, TASK-002-010
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Akka.Persistence journal/snapshot store initialized at start
- [ ] ConfigurationPersistenceActor recovers state from journal
- [ ] JSON config file imported if present (merged into persisted state)
- [ ] Env vars override persisted/JSON config
- [ ] ConfigurationUpdateRouter is started
- [ ] Correct order: Persistence recovery -> JSON -> Env -> Router start
- [ ] Integration test for boot sequence
- [ ] Typecheck/lint passes

## Task Dependency Graph

```
TASK-002-001 --> TASK-002-002 --> TASK-002-003 --> TASK-002-004 --> TASK-002-005
                                                                     |--> TASK-002-006 --> TASK-002-011
                                                                     |--> TASK-002-007 --> TASK-002-009 --> TASK-002-011
                                                                     |--> TASK-002-008 --/
                                                                     \--> TASK-002-010 --> TASK-002-011
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-002-001 | ~15k | none | yes | haiku |
| TASK-002-002 | ~50k | 001 | no | opus |
| TASK-002-003 | ~20k | 002 | no | — |
| TASK-002-004 | ~40k | 002, 003 | no | — |
| TASK-002-005 | ~50k | 004 | no | — |
| TASK-002-006 | ~40k | 005 | yes (with 007, 008) | opus |
| TASK-002-007 | ~30k | 005 | yes (with 006, 008) | — |
| TASK-002-008 | ~20k | 005 | yes (with 006, 007) | — |
| TASK-002-009 | ~35k | 005, 007, 008 | no | — |
| TASK-002-010 | ~30k | 005 | yes (with 009) | — |
| TASK-002-011 | ~25k | 006, 009, 010 | no | — |

**Total estimated tokens:** ~355k

## Functional Requirements

- FR-1: All domain configurations must be persisted via Akka.Persistence and survive restarts
- FR-2: JSON config file import/export must be roundtrip-capable
- FR-3: Configuration changes via API must take effect immediately (no restart)
- FR-4: Environment variables must override JSON/persisted configuration
- FR-5: `DOMAINS` env var must support https-portal-compatible format
- FR-6: Invalid configurations must be rejected with a clear error message
- FR-7: Configuration changes must notify all affected actors via EventStream and RoundRobinRouter

## Non-Goals (Out of Scope)

- No multi-user / role-based access to config API (comes with Feature 004)
- No config versioning / rollback functionality
- No remote configuration (etcd, Consul, etc.)
- No YAML support (JSON only)
- No Entity Framework or direct database access — Akka.Persistence handles all persistence

## Technical Considerations

- Akka.Persistence with SQLite or file-based journal/snapshot store — no separate database server needed
- Event-sourced state: all changes are persisted as events, state is rebuilt on recovery
- Snapshots for fast recovery (avoid replaying entire event history)
- RoundRobinRouter for efficient distribution of configuration updates to multiple subscriber actors
- JSON serialization via System.Text.Json (no Newtonsoft)
- Config file path via `--config` CLI argument or `CONFIG_FILE` env var

## Success Metrics

- Config changes via API are effective within <100ms
- JSON import/export roundtrip loses no data
- Env var override works correctly on container start
- All CRUD operations with correct validation
- Actor state fully recovered after restart

## Open Questions

*No open questions.*
