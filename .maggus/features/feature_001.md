<!-- maggus-id: e27a6717-f341-4396-ad18-990d677d44e8 -->

# Feature 001: Reverse Proxy Core

## Introduction

Implementation of the core reverse proxy with Kestrel as edge server and TurboHTTP as HTTP client for upstream forwarding. Domain-based routing, load balancing, WebSocket proxying, and health checks.

### Architecture Context

- **Vision:** Schleusenwerk replaces https-portal as a .NET-based HTTPS reverse proxy with management UI
- **New Components:** DomainRouterActor, HealthCheckActor, RequestForwardingPipeline, WebSocket tunnel
- **Akka.NET:** Actor-based state management for routing tables and upstream health
- **TurboHTTP:** NuGet package `TurboHTTP` — channel-based API for high-throughput proxying

## Goals

- Route incoming HTTP/HTTPS requests to configured upstreams based on the Host header
- Round-robin load balancing across multiple upstreams per domain
- Transparently proxy WebSocket connections
- Set standard proxy headers (X-Forwarded-For, X-Real-IP, X-Forwarded-Proto)
- HTTP-to-HTTPS redirect with configurable status code (301/307)
- Periodic health checks of upstreams with automatic removal of unhealthy targets

## Tasks

### TASK-001-001: Domain Routing Data Model
**Description:** As a developer I want strongly typed models for domain configurations, upstream definitions, and routing rules so the proxy logic builds on clean data structures.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-001-002, TASK-001-003
**Parallel:** yes — can run alongside other features

**Acceptance Criteria:**
- [x] `DomainConfig` record with domain name, redirect mode, options
- [x] `UpstreamTarget` record with URL, weight, max connections
- [x] `RouteDefinition` record mapping domain to upstream(s)
- [x] Value objects for DomainName, UpstreamUrl with validation
- [x] All models are immutable records
- [x] Typecheck/lint passes

---

### TASK-001-002: DomainRouter Actor
**Description:** As a reverse proxy I want an Akka.NET actor that manages the routing table and makes routing decisions so that configuration changes are processed thread-safe and without restart.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-005
**Parallel:** yes — can run alongside TASK-001-003
**Model:** opus

**Acceptance Criteria:**
- [x] `DomainRouterActor` with in-memory routing state
- [x] Messages: `UpdateRoutes`, `ResolveUpstream`, `RemoveDomain`
- [x] Routing lookup by Host header in O(1) via Dictionary
- [x] Publishes events on routing changes via EventStream
- [x] Unit tests with Akka.TestKit
- [x] Typecheck/lint passes

---

### TASK-001-003: Round-Robin Load Balancer (Akka.NET Router)
**Description:** As a reverse proxy I want to distribute requests evenly across multiple upstreams using Akka.NET's built-in RoundRobinRouter so the load is balanced without a custom implementation.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-005
**Parallel:** yes — can run alongside TASK-001-002

**Acceptance Criteria:**
- [x] Uses Akka.NET `RoundRobinPool` or `RoundRobinGroup` router for upstream distribution
- [x] Considers weight parameter per upstream (via routee configuration)
- [x] Skips unhealthy upstreams (dynamic routee removal/addition)
- [x] Unit tests for distribution and edge cases (single upstream, all unhealthy)
- [x] Typecheck/lint passes

---

### TASK-001-004: TurboHTTP Integration & Request Forwarding Pipeline
**Description:** As a reverse proxy I want to forward incoming Kestrel requests to upstreams via TurboHTTP so the responses are streamed back to the client.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-005, TASK-001-006
**Parallel:** yes — can run alongside TASK-001-002 and TASK-001-003
**Model:** opus

**Acceptance Criteria:**
- [x] TurboHTTP integrated as NuGet package reference
- [x] `RequestForwardingPipeline` converts Kestrel HttpContext to HttpRequestMessage
- [x] Response body is streamed (not buffered)
- [x] Proxy headers set: X-Forwarded-For, X-Real-IP, X-Forwarded-Proto, X-Forwarded-Host
- [x] Hop-by-hop headers correctly filtered (Connection, Keep-Alive, etc.)
- [x] Timeout handling for upstream connections
- [x] Error handling: 502 Bad Gateway on upstream error, 504 Gateway Timeout
- [x] Unit tests for header manipulation
- [x] Typecheck/lint passes

---

### TASK-001-005: Proxy Request Handler (Main Entry Point)
**Description:** As a reverse proxy I want a central request handler that orchestrates Kestrel route -> DomainRouter -> LoadBalancer -> ForwardingPipeline so that all proxy components work together.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-001-002, TASK-001-003, TASK-001-004
**Successors:** TASK-001-007, TASK-001-008
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `ProxyRequestHandler` as ASP.NET Core middleware or endpoint
- [ ] Catch-all route for all incoming requests
- [ ] Queries DomainRouterActor for upstream via Ask pattern
- [ ] Selects upstream via LoadBalancer
- [ ] Forwards via RequestForwardingPipeline
- [ ] HTTP-to-HTTPS redirect when configured
- [ ] 404 when domain is not configured
- [ ] Registered in Program.cs
- [ ] Typecheck/lint passes

---

### TASK-001-006: Header Manipulation Filter
**Description:** As an operator I want to manipulate response headers before sending them to the client so that upstream server details are hidden.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-001-004
**Successors:** TASK-001-008
**Parallel:** yes — can run alongside TASK-001-005

**Acceptance Criteria:**
- [ ] `HeaderManipulationFilter` with configurable add/remove/replace rules
- [ ] Removes Server, X-Powered-By headers by default
- [ ] Sets Via header with proxy identification
- [ ] Unit tests
- [ ] Typecheck/lint passes

---

### TASK-001-007: WebSocket Proxy
**Description:** As an operator I want WebSocket connections to be transparently tunneled through the proxy so that real-time applications work.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-001-005
**Successors:** TASK-001-008
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] Detection of WebSocket upgrade requests (Upgrade: websocket header)
- [ ] Bidirectional stream tunnel between client and upstream
- [ ] Correct header forwarding during upgrade
- [ ] Timeout and connection close handling
- [ ] Unit tests with mock WebSocket
- [ ] Typecheck/lint passes

---

### TASK-001-008: Health Check Actor
**Description:** As an operator I want the proxy to periodically check upstream reachability so that unhealthy targets are automatically removed from the pool.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-001-002
**Successors:** none
**Parallel:** yes — can run alongside TASK-001-005, TASK-001-006, TASK-001-007

**Acceptance Criteria:**
- [ ] `HealthCheckActor` with configurable interval (default 30s)
- [ ] HTTP HEAD or GET to configured health endpoint
- [ ] Marks upstream as healthy/unhealthy
- [ ] Publishes `UpstreamHealthChanged` events via EventStream
- [ ] DomainRouterActor reacts to health events
- [ ] Configurable thresholds (failures before unhealthy, successes before healthy)
- [ ] Unit tests with Akka.TestKit
- [ ] Typecheck/lint passes

---

### TASK-001-009: Integration Tests
**Description:** As a developer I want end-to-end tests for the reverse proxy so I can be confident that routing, load balancing, and headers work correctly.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-001-005, TASK-001-006, TASK-001-007, TASK-001-008
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Test: Request to Domain A -> Upstream A, Domain B -> Upstream B
- [ ] Test: Round-robin distribution across 3 upstreams
- [ ] Test: Proxy headers correctly set
- [ ] Test: HTTP-to-HTTPS redirect
- [ ] Test: 404 for unknown domain
- [ ] Test: WebSocket tunnel works
- [ ] Test: Unhealthy upstream is skipped
- [ ] All tests green
- [ ] Typecheck/lint passes

## Task Dependency Graph

```
TASK-001-001 --> TASK-001-002 --> TASK-001-005 --> TASK-001-007 --> TASK-001-009
             |-> TASK-001-003 --/                                  ^
             \-> TASK-001-004 --> TASK-001-006 --------------------/
                            \---> TASK-001-005
TASK-001-002 --> TASK-001-008 ------------------------------------|
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-001-001 | ~25k | none | yes | — |
| TASK-001-002 | ~50k | 001 | yes (with 003, 004) | opus |
| TASK-001-003 | ~20k | 001 | yes (with 002, 004) | — |
| TASK-001-004 | ~75k | 001 | yes (with 002, 003) | opus |
| TASK-001-005 | ~40k | 002, 003, 004 | no | opus |
| TASK-001-006 | ~25k | 004 | yes (with 005) | — |
| TASK-001-007 | ~50k | 005 | no | opus |
| TASK-001-008 | ~40k | 002 | yes (with 005, 006, 007) | — |
| TASK-001-009 | ~50k | 005, 006, 007, 008 | no | — |

**Total estimated tokens:** ~375k

## Functional Requirements

- FR-1: The proxy must route incoming requests to configured upstreams based on the Host header
- FR-2: With multiple upstreams per domain, round-robin load balancing with weight support must be applied
- FR-3: Proxy headers (X-Forwarded-For, X-Real-IP, X-Forwarded-Proto, X-Forwarded-Host) must be set
- FR-4: HTTP-to-HTTPS redirect must use a configurable status code (301/307)
- FR-5: WebSocket upgrade requests must be detected and bidirectionally tunneled
- FR-6: Unhealthy upstreams must be automatically removed from the load balancing pool
- FR-7: Upstream errors must return 502 Bad Gateway, timeouts must return 504 Gateway Timeout
- FR-8: Unknown domains must return 404

## Non-Goals (Out of Scope)

- No HTTP/3 QUIC support in V1 (may come later via TurboHTTP)
- No sticky sessions / session affinity (round-robin only)
- No automatic retry on upstream errors (possible later)
- No request/response body transformation
- No caching of upstream responses

## Technical Considerations

- TurboHTTP is integrated as a NuGet package (`TurboHTTP`)
- Kestrel as edge server — no separate nginx required
- Akka.NET actors for thread-safe state management (no locking needed)
- Response body must be streamed (not buffered in memory) for large responses
- WebSocket tunnel requires bidirectional streaming with System.Net.WebSockets

## Success Metrics

- Proxy correctly forwards requests to configured upstreams
- Load balancing distributes requests evenly (+/-5%)
- WebSocket connections work without interruption
- Health checks detect failed upstreams within 2 cycles
- All integration tests green

## Open Questions

*No open questions — all clarified through user input.*
