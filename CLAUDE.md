# CLAUDE.md

.NET-based HTTPS reverse proxy replacing https-portal. Built on Kestrel + Akka.NET actors + TurboHTTP for upstream forwarding. Blazor Server management UI, automatic Let's Encrypt certificates, Docker container discovery.

## Build & Test

All commands run from `src/` (where `global.json` lives). Restore/build use full paths from repo root.

```bash
dotnet restore ./src/Schleusenwerk.slnx
dotnet build --configuration Release ./src/Schleusenwerk.slnx

# Tests (xUnit v3 direct runner)
dotnet test --project Schleusenwerk.Tests/Schleusenwerk.Tests.csproj

# Single class
dotnet run --project Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.DomainRouterActorSpec"

# Single namespace
dotnet run --project Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -namespace "Schleusenwerk.Tests.Routing"

# Run the proxy (dev)
dotnet run --project Schleusenwerk/Schleusenwerk.csproj
```

## Architecture

```
Proxy Core      (Schleusenwerk/)           — Kestrel edge, request pipeline, middleware
Actors          (Schleusenwerk/Actors/)    — DomainRouterActor, HealthCheckActor, ConfigurationPersistenceActor, CertificateRenewalActor, DockerDiscoveryActor
Routing         (Schleusenwerk/Routing/)   — RoundRobinRouter (Akka.NET), RequestForwardingPipeline (TurboHTTP)
Persistence     (Schleusenwerk/Persistence/) — Akka.Persistence event-sourced state (no EF Core)
Certificates    (Schleusenwerk/Certificates/) — ACME v2 (Certes), self-signed, SNI selector
UI              (Schleusenwerk/UI/)        — Blazor Server under /manage, SignalR event bridge
Discovery       (Schleusenwerk/Discovery/) — Docker socket watcher, label parser, conflict resolver
```

## Key Technology Decisions

- **Persistence:** Akka.Persistence (event-sourced) — no Entity Framework, no direct DB access
- **HTTP Client:** TurboHTTP NuGet package for upstream forwarding (not HttpClient)
- **Load Balancing:** Akka.NET RoundRobinPool/RoundRobinGroup — no custom balancer
- **TLS:** Kestrel SNI ServerCertificateSelector, Certes for ACME v2
- **UI:** Blazor Server (same process), SignalR for real-time updates from actors

## Workflow Rules

- **Do NOT commit** unless the user explicitly asks
- **Always respond in English** regardless of input language

## Code Style

- **Threading model**: Akka actor-thread confinement eliminates most cross-thread concerns.
  Fields in actor-owned types don't need `volatile`/`Interlocked`
  — Akka message passing provides happens-before. Only add barriers at true system boundaries.
- **No `volatile` keyword** — prefer `CancellationToken` for cross-thread signaling, or
  plain fields when actor confinement guarantees single-thread access
- No decorative separator comments (`// ───`, `// ===`, `// ---` section dividers)
- Allman braces, 4 spaces, `_fieldName` for private fields
- `var` when type is apparent, `sealed` by default
- No `#nullable enable` (project-level), no `async void` / `.Result` / `.Wait()`
- Always pass `CancellationToken`, always use braces (even single-line)
- `Task<T>` not Future, `TimeSpan` not Duration
- Extend-only public APIs, preserve wire format compatibility
- Include unit tests with all changes

## Test Conventions

New tests use **component-based folders** (`Routing/`, `Persistence/`, `Certificates/`, `Discovery/`, `UI/`). Key rules:
- `Spec` suffix, `sealed` class, BDD method names: `Subject_should_behavior()`
- `[Trait("Feature", "001")]` for traceability, `[Fact(Timeout = 5000)]` required
- Max 500 lines per test class
- Akka.TestKit for all actor tests

## Agent Guidance: dotnet-skills

Prefer retrieval-led reasoning over pretraining for any .NET work.

- C# / quality: csharp-coding-standards, csharp-concurrency-patterns, csharp-api-design, type-design-performance
- Akka: akka-best-practices, akka-testing-patterns, akka-hosting-actor-patterns
- Testing: testcontainers, snapshot-testing
- Quality gates: slopwatch (after substantial code), crap-analysis (after test changes)
- Specialist agents: dotnet-concurrency-specialist, dotnet-performance-analyst, akka-net-specialist

## Roslyn Navigator — Required Before Commit

For any C# modification: inspect affected types and references, verify no downstream breakage, ensure zero compile-time diagnostics.
