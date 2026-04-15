<!-- maggus-id: b770ed93-a8bf-4937-9661-af733463d5bc -->

# Feature 005: Docker Container Discovery

## Introduction

Automatic detection of Docker containers via the Docker socket (`/var/run/docker.sock`). Containers with `VIRTUAL_HOST` and `VIRTUAL_PORT` labels are automatically registered as proxy routes. Routes are dynamically updated on container start/stop.

### Architecture Context

- **Vision:** Zero-config deployment — start containers and they are immediately reachable via HTTPS
- **New Components:** DockerDiscoveryActor, DockerClient, LabelParser, ConflictResolver
- **Akka.NET:** DockerDiscoveryActor monitors Docker events and propagates route changes
- **Dependencies:** Feature 001 (routing), Feature 002 (config persistence via Akka.Persistence)
- **Security:** Docker socket access effectively grants root privileges — must be documented

## Goals

- Automatically register containers with VIRTUAL_HOST/VIRTUAL_PORT labels as proxy routes
- Process container start/stop events in real-time
- Conflict resolution between manually configured and auto-discovered routes
- Consider container health status (only route healthy containers)
- Graceful degradation when Docker socket is unavailable

## Tasks

### TASK-005-001: Docker API Client
**Description:** As a system I want to communicate with the Docker API via the Unix socket so I can query containers and events.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-005-002, TASK-005-003
**Parallel:** yes

**Acceptance Criteria:**
- [ ] `Docker.DotNet` NuGet package in Directory.Packages.props
- [ ] Docker client wrapper with socket configuration
- [ ] `ListContainers()` — all running containers with labels
- [ ] `MonitorEvents()` — stream of container events (start, stop, die)
- [ ] Connection timeout and retry on socket problems
- [ ] Graceful fallback when socket is not present (warning log, discovery disabled)
- [ ] Unit tests with mocked Docker client
- [ ] Typecheck/lint passes

---

### TASK-005-002: Label Parser & Route Generation
**Description:** As a system I want to parse Docker container labels and generate valid proxy routes from them so auto-discovered containers are routed correctly.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-005-001
**Successors:** TASK-005-003
**Parallel:** no

**Acceptance Criteria:**
- [ ] Parses `VIRTUAL_HOST` (comma-separated domains)
- [ ] Parses `VIRTUAL_PORT` (default: 80)
- [ ] Parses `VIRTUAL_PROTO` (http/https, default: http)
- [ ] Generates `RouteDefinition` objects with Source = Docker
- [ ] Validation (invalid domains/ports are logged as warning and skipped)
- [ ] Support for multiple domains per container
- [ ] Unit tests for various label combinations
- [ ] Typecheck/lint passes

---

### TASK-005-003: Docker Discovery Actor
**Description:** As an Akka.NET system I want an actor that continuously monitors Docker events and updates routes so container changes are reflected in real-time in the proxy.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-005-001, TASK-005-002
**Successors:** TASK-005-004, TASK-005-005
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `DockerDiscoveryActor` with states: Initializing, Watching, Disconnected
- [ ] On start: scan all running containers and register routes
- [ ] Event stream: container start -> add route, stop/die -> remove route
- [ ] Periodic re-sync (default: 60s) as fallback in case events are missed
- [ ] Supervision strategy: restart on socket error with exponential backoff
- [ ] Publishes events: ContainerDiscovered, ContainerRemoved
- [ ] Logging of all discovery activities
- [ ] Unit tests with Akka.TestKit
- [ ] Typecheck/lint passes

---

### TASK-005-004: Conflict Resolution (Manual vs Docker Routes)
**Description:** As an operator I want clear rules when a domain is both manually configured and detected via Docker discovery so there are no unexpected routing conflicts.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-005-003
**Successors:** TASK-005-006
**Parallel:** yes — can run alongside TASK-005-005

**Acceptance Criteria:**
- [ ] `RouteSource` enum: Manual, Docker
- [ ] Manual routes have priority over Docker routes
- [ ] On conflict: warning in logs, Docker route is ignored
- [ ] Configurable: manual-priority or docker-priority
- [ ] Blacklist for container labels that should be ignored
- [ ] Unit tests for conflict scenarios
- [ ] Typecheck/lint passes

---

### TASK-005-005: Container Health & Readiness
**Description:** As a proxy I want to only route to healthy containers so no requests go to still-starting or failing containers.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-005-003
**Successors:** TASK-005-006
**Parallel:** yes — can run alongside TASK-005-004

**Acceptance Criteria:**
- [ ] Query Docker container health status (HEALTHCHECK result)
- [ ] Grace period after container start (configurable, default: 5s)
- [ ] Unhealthy containers are removed from routing
- [ ] Containers without HEALTHCHECK are considered healthy
- [ ] Health status changes trigger route updates
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-005-006: Docker Discovery Integration & Tests
**Description:** As a developer I want end-to-end tests for Docker discovery so I can be confident that containers are correctly detected and routed.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-005-004, TASK-005-005
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Integration in Program.cs (DockerDiscoveryActor is started when socket is available)
- [ ] Discovery disableable via settings
- [ ] Docker discovery status visible in dashboard (Feature 004)
- [ ] Test: Start container with VIRTUAL_HOST -> route appears
- [ ] Test: Stop container -> route disappears
- [ ] Test: Manual override has priority
- [ ] Test: Unhealthy container is not routed
- [ ] All tests green
- [ ] Typecheck/lint passes

## Task Dependency Graph

```
TASK-005-001 --> TASK-005-002 --> TASK-005-003 --> TASK-005-004 --> TASK-005-006
                                               \-> TASK-005-005 --/
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-005-001 | ~40k | none | yes | — |
| TASK-005-002 | ~25k | 001 | no | — |
| TASK-005-003 | ~50k | 001, 002 | no | opus |
| TASK-005-004 | ~30k | 003 | yes (with 005) | — |
| TASK-005-005 | ~25k | 003 | yes (with 004) | — |
| TASK-005-006 | ~40k | 004, 005 | no | — |

**Total estimated tokens:** ~210k

## Functional Requirements

- FR-1: Containers with `VIRTUAL_HOST` label must be automatically registered as proxy routes
- FR-2: Container start/stop events must be processed within 5 seconds
- FR-3: Manually configured routes must have priority over auto-discovered routes
- FR-4: Containers without HEALTHCHECK or with healthy status must be routed
- FR-5: Discovery must be gracefully disableable when Docker socket is unavailable
- FR-6: Periodic re-sync must correct missing/surplus routes

## Non-Goals (Out of Scope)

- No Docker Compose specific support (container labels only)
- No Kubernetes support (Docker socket only)
- No automatic Docker network setup
- No container management functions (start/stop/restart via UI)

## Technical Considerations

- `Docker.DotNet` library for Docker API communication
- Unix socket only on Linux — Windows Docker Desktop uses named pipes
- Mount Docker socket in container: `-v /var/run/docker.sock:/var/run/docker.sock:ro`
- Security warning: Docker socket access = root privileges on the host
- Container IP resolution: Docker DNS (container name) preferred over IP addresses
- Akka supervision: DockerDiscoveryActor with `SupervisorStrategy.StoppingStrategy` -> backoff restart

## Success Metrics

- Containers with VIRTUAL_HOST are registered as routes within 5 seconds of start
- Container stop leads to route removal within 5 seconds
- No traffic to unhealthy or stopped containers
- Manually configured domains remain unaffected by discovery

## Open Questions

*No open questions.*
