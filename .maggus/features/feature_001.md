<!-- maggus-id: aca97178-f176-472e-924c-902a52b116ff -->

# Feature 001: Infrastructure Foundation

## Introduction

Establish the long-term structural foundation for Schleusenwerk. The current codebase has all source in a single `Schleusenwerk` project — domain models, actor implementations, persistence, and HTTP forwarding are co-located. There is no `Schleusenwerk.Core` for shared contracts, no `Schleusenwerk.UI` for the management frontend, and no Docker Compose to run the system. This feature creates the correct project layout, moves shared types to their permanent home, wires SQLite persistence via the C# Akka.Hosting API, and produces Dockerfiles + a Docker Compose configuration so that `docker compose up` starts both containers.

### Architecture Context

- **Vision alignment:** Single-instance proxy with SQLite, Blazor UI in a separate container — this feature creates the structural prerequisites for everything that follows
- **Components touched:** All four projects (`Schleusenwerk`, `Schleusenwerk.Core`, `Schleusenwerk.UI`, `Schleusenwerk.Tests`)
- **New patterns introduced:** Shared contracts in `Schleusenwerk.Core`; multi-stage Dockerfiles; Docker volume mounts for SQLite DB and certificates

## Goals

- Solution contains all four projects with correct project references and compiles cleanly
- Domain models, commands, events, results, and config types live in `Schleusenwerk.Core`; actor-internal messages remain in `Schleusenwerk`
- Akka.Persistence is configured for SQLite via the C# Akka.Hosting API — no HOCON anywhere
- `docker compose up` starts the proxy and UI containers with correct volume mounts

## Tasks

### TASK-001-001: Create Schleusenwerk.Core and move shared types

**Description:** As a developer, I want all shared domain contracts in a dedicated `Schleusenwerk.Core` project so that both the proxy and future consumers have a single authoritative source for domain types.

**Token Estimate:** ~75k tokens
**Predecessors:** none
**Successors:** TASK-001-004
**Parallel:** yes — can run alongside TASK-001-002 and TASK-001-003

**What moves to Core:**

| Namespace / File | Type |
|---|---|
| `Routing/DomainName.cs` | Domain model |
| `Routing/UpstreamUrl.cs` | Domain model |
| `Routing/UpstreamTarget.cs` | Domain model |
| `Routing/RouteDefinition.cs` | Domain model |
| `Routing/DomainConfig.cs` | Domain model |
| `Routing/RedirectMode.cs` | Domain model |
| `IClusterEvent.cs` | Marker interface |
| `Persistence/ConfigurationCommands.cs` | Commands |
| `Persistence/ConfigurationEvents.cs` | Events |
| `Persistence/ConfigurationResult.cs` | Result types |
| `Persistence/ConfigurationException.cs` | Exception |
| `Persistence/ProxySettings.cs` | Config type |
| `Persistence/CertificateInfo.cs` | Config type |
| `Persistence/AcmeStage.cs` | Enum |
| `HealthCheck/HealthCheckConfig.cs` | Config type |

**What stays in Schleusenwerk:**
- All actor implementations
- `Routing/DomainRouterMessages.cs` (actor-internal: `ResolveUpstream`, `UpstreamResolved`, `UpdateRoutes`, etc.)
- `LoadBalancing/Messages.cs` (actor-internal: `SelectUpstream`, `MarkUpstreamHealthy`, etc.)
- `HealthCheck/HealthCheckMessages.cs` (actor-internal probe messages)
- `Persistence/ConfigurationState.cs`, `ConfigurationExporter.cs`, `EventHub.cs` (implementation details)

**Acceptance Criteria:**
- [x] `Schleusenwerk.Core` project exists as a class library targeting net10.0
- [x] All types listed above are in `Schleusenwerk.Core` under their original namespaces (e.g. `Schleusenwerk.Routing`, `Schleusenwerk.Persistence`)
- [x] `Schleusenwerk.csproj` has a project reference to `Schleusenwerk.Core`
- [x] `Schleusenwerk.Tests.csproj` has a project reference to `Schleusenwerk.Core`
- [x] `Schleusenwerk.slnx` includes `Schleusenwerk.Core`
- [x] `dotnet build` produces zero errors and zero warnings
- [x] All existing tests pass

---

### TASK-001-002: Create Schleusenwerk.UI project

**Description:** As a developer, I want a `Schleusenwerk.UI` Blazor Server project in the solution so that the management frontend has a proper home with the correct dependencies.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-001-004
**Parallel:** yes — can run alongside TASK-001-001 and TASK-001-003

**Acceptance Criteria:**
- [x] `Schleusenwerk.UI` exists as a Blazor Server project targeting net10.0
- [x] Project references `Schleusenwerk.Core`
- [x] `Schleusenwerk.slnx` includes `Schleusenwerk.UI`
- [x] Default Blazor Server scaffold is present (Program.cs, App.razor, layout)
- [x] Project includes a placeholder home page that displays "Schleusenwerk UI" so the container is visibly running
- [x] `dotnet run --project src/Schleusenwerk.UI/Schleusenwerk.UI.csproj` starts without errors

---

### TASK-001-003: Configure SQLite Akka.Persistence

**Description:** As a developer, I want Akka.Persistence configured for SQLite via the C# Akka.Hosting API so that all actor state is persisted locally without an external database.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-001-004
**Parallel:** yes — can run alongside TASK-001-001 and TASK-001-002

**Details:**
- Add `Akka.Persistence.Sqlite` NuGet package to `Schleusenwerk`
- Configure journal and snapshot store in `SchleusenwerkActorSystemSetup` using `WithSqlitePersistence(connectionString)` (or the equivalent Akka.Hosting extension — check package API)
- Connection string comes from `IConfiguration` (key: `Akka:Persistence:ConnectionString`), with a sensible default for local dev (e.g. `Data Source=/data/schleusenwerk.db`)
- Remove any PostgreSQL persistence packages and configuration
- `EventHub` is currently registered as `WithSingleton` with `ClusterSingletonOptions` — this requires Akka.Cluster. For single-instance MVP, change it to a regular actor registered via `WithActors`. No cluster dependency.

**Acceptance Criteria:**
- [~] `Akka.Persistence.Sqlite` is referenced in `Schleusenwerk.csproj` — SUBSTITUTED: used `Akka.Persistence.Sql.Hosting` with `ProviderName.SQLiteMS`; classic `Akka.Persistence.Sqlite` has no Akka.Hosting C# extension, only HOCON — which CLAUDE.md prohibits
- [x] No PostgreSQL persistence packages remain in any project
- [x] `SchleusenwerkActorSystemSetup` configures journal + snapshot store for SQLite via C# API — no HOCON strings
- [x] `EventHub` is a plain local actor (not a Cluster Singleton); `Akka.Cluster.Hosting` is removed if no longer needed
- [x] Connection string is read from `IConfiguration` with a `/data/schleusenwerk.db` default
- [x] `dotnet build` produces zero errors
- [x] All existing persistence tests pass (Akka.Persistence.TestKit base class handles test journal — do not add manual HOCON)

---

### TASK-001-004: Dockerfiles for Proxy and UI

**Description:** As a developer, I want multi-stage Dockerfiles for both containers so that each project can be built and run as an isolated Docker image.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-001-001, TASK-001-002
**Successors:** TASK-001-005
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [x] `src/Schleusenwerk/Dockerfile` exists — multi-stage build (sdk → runtime), publishes to `/app`, exposes ports 80, 443, 5000
- [x] `src/Schleusenwerk.UI/Dockerfile` exists — multi-stage build (sdk → runtime), publishes to `/app`, exposes port 8080
- [x] Both Dockerfiles set `ASPNETCORE_ENVIRONMENT=Production` by default
- [x] Build context is the repo root (so project references across `src/` resolve correctly)
- [~] ⚠️ BLOCKED: `docker build -f src/Schleusenwerk/Dockerfile .` succeeds — Docker is not available in this environment
- [~] ⚠️ BLOCKED: `docker build -f src/Schleusenwerk.UI/Dockerfile .` succeeds — Docker is not available in this environment

---

### TASK-001-005: Docker Compose

**Description:** As a developer, I want a `docker-compose.yml` at the repo root so that `docker compose up` starts the proxy and UI with correct volume mounts and environment wiring.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-001-004
**Successors:** none
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [ ] `docker-compose.yml` exists at repo root
- [ ] `proxy` service uses `src/Schleusenwerk/Dockerfile`, mounts `/var/run/docker.sock`, a named volume for SQLite (`db:/data`), and a named volume for certificates (`certs:/certs`); exposes ports 80, 443, 5000
- [ ] `ui` service uses `src/Schleusenwerk.UI/Dockerfile`, exposes port 8080, has `PROXY_GRPC_ENDPOINT=http://proxy:5000` env var
- [ ] Named volumes `db` and `certs` are declared
- [ ] `docker compose up` starts both containers without errors
- [ ] Proxy container logs show Akka actor system started
- [ ] UI container serves the placeholder home page on port 8080

---

## Task Dependency Graph

```
TASK-001-001 ──┐
TASK-001-002 ──┼──→ TASK-001-004 ──→ TASK-001-005
TASK-001-003 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-001-001 | ~75k | none | yes (mit 002, 003) | — |
| TASK-001-002 | ~35k | none | yes (mit 001, 003) | — |
| TASK-001-003 | ~30k | none | yes (mit 001, 002) | — |
| TASK-001-004 | ~20k | 001, 002 | no | haiku |
| TASK-001-005 | ~20k | 004 | no | haiku |

**Total estimated tokens:** ~180k

## Functional Requirements

- FR-1: `Schleusenwerk.Core` is a class library that contains all domain models, commands, events, result types, and config types listed in TASK-001-001
- FR-2: `Schleusenwerk.UI` is a Blazor Server app that references `Schleusenwerk.Core` and renders a visible placeholder page
- FR-3: Akka.Persistence in `Schleusenwerk` uses SQLite as journal and snapshot store, configured via C# API only — no HOCON
- FR-4: The SQLite connection string is configurable via `IConfiguration`; the default points to `/data/schleusenwerk.db`
- FR-5: `EventHub` runs as a plain local singleton actor — no Akka.Cluster dependency for MVP
- FR-6: Both projects have multi-stage Dockerfiles that build from the repo root as context
- FR-7: `docker compose up` starts both containers with SQLite and cert volumes mounted

## Non-Goals

- No gRPC scaffolding (proto files, server/client stubs) — that is a separate feature
- No OpenTelemetry wiring
- No authentication or TLS configuration in the UI
- No actual Blazor UI components beyond the placeholder page
- No CI/CD pipeline
- No health check endpoints in Dockerfiles (can be added later)

## Technical Considerations

- The `.slnx` solution format is used — use `dotnet sln` commands or edit the file directly; do not convert to `.sln`
- Build commands run from `src/` where `global.json` lives; Docker build context must be the repo root so that `../` project references work in multi-stage builds
- `Akka.Persistence.TestKit` auto-configures the in-memory test journal — do not add SQLite configuration to tests; it would break the TestKit setup
- `EventHub` currently uses `WithSingleton` with `ClusterSingletonOptions` — removing this also removes the implicit Akka.Cluster dependency; verify no other cluster APIs remain after the change
- Namespaces in `Schleusenwerk.Core` should mirror the originals (e.g. `Schleusenwerk.Routing`, `Schleusenwerk.Persistence`) so that moves are transparent to the compiler

## Success Metrics

- `dotnet build --configuration Release ./src/Schleusenwerk.slnx` exits with code 0, zero warnings
- `dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj` — all tests pass
- `docker compose up` starts both containers; proxy logs show Akka started; UI responds on port 8080

## Open Questions

*(none)*
