# Aspire Docker Integration Tests — Design Spec

## Summary

Replace the empty IntegrationTests project with real Docker-based integration tests using Aspire's `DistributedApplicationTestingBuilder`. Both the proxy and web containers are built from their Dockerfiles and started as real containers. All tests go through the nginx reverse proxy (web container) to test the actual production path.

## Test Infrastructure

### Container Topology

```
Test Runner (xUnit)
  └── Aspire TestingBuilder
        ├── proxy (AddDockerfile → src/Schleusenwerk/Dockerfile)
        │     Port 5000 (REST API + SignalR)
        │     ASPNETCORE_ENVIRONMENT=Development
        │     SQLite in-memory, temp certs dir
        │
        └── web (AddDockerfile → src/Schleusenwerk.Web/Dockerfile)
              Port 80 (nginx → proxy:5000)
              Depends on: proxy
```

### Fixture

`SchleusenwerkFixture` implements `IAsyncLifetime`:
- `InitializeAsync`: Starts Aspire app with both containers, waits for health endpoint through nginx (`/api/health`), creates shared `HttpClient` pointing at the web container
- `DisposeAsync`: Stops and cleans up containers
- Provides `HttpClient` (base URL = web container) and `CreateHubConnection()` factory for SignalR
- Tests use `IClassFixture<SchleusenwerkFixture>` — containers start once per test class

### Health Wait

The fixture polls `GET /api/health` through nginx every 2 seconds for up to 60 seconds. This confirms both containers are running and nginx is proxying correctly.

## Test Classes

### RouteManagementSpec (7 tests)
- `ListRoutes_should_return_empty_initially`
- `AddRoute_should_create_and_return_success`
- `ListRoutes_should_contain_created_route`
- `GetRoute_should_return_detail`
- `UpdateRoute_should_change_config`
- `DeleteRoute_should_remove_route`
- `GetRoute_should_return_404_for_unknown_domain`

### UpstreamManagementSpec (3 tests)
- `AddUpstream_should_appear_in_route_detail`
- `RemoveUpstream_should_disappear_from_route_detail`
- `AddUpstream_to_nonexistent_route_should_fail`

### CertificateSpec (3 tests)
- `ListCertificates_should_return_empty_initially`
- `ProvisionCertificate_should_trigger_selfsigned`
- `ListCertificates_should_contain_provisioned_cert`

### HealthSpec (2 tests)
- `GetHealth_should_return_counts`
- `GetUpstreamHealth_should_return_entries_for_domain`

### DiscoverySpec (1 test)
- `ListContainers_should_return_ok`

### SignalRSpec (1 test)
- `Should_receive_event_when_route_created` — connect to `/hubs/events` through nginx, create a route via REST, assert event received within 10 seconds

### NginxProxySpec (3 tests)
- `Api_routes_should_be_proxied_through_nginx` — `GET /api/routes` returns 200
- `SignalR_hub_should_be_reachable_through_nginx` — negotiate endpoint returns 200
- `Root_should_return_index_html` — `GET /` returns HTML with `<div id="app">`

### Total: 20 tests

## Project Setup

- `Schleusenwerk.IntegrationTests.csproj` references `Aspire.Hosting`, `Aspire.Hosting.Testing`, `xunit.v3.mtp-v2`, `Microsoft.AspNetCore.SignalR.Client`
- No project reference to `Schleusenwerk` or `Schleusenwerk.Web` — containers are built from Dockerfiles
- Does reference `Schleusenwerk.AppHost` for the Aspire app model (but AppHost itself needs to be refactored to use `AddDockerfile` instead of `AddProject`)
- All HTTP communication uses `System.Net.Http.HttpClient` with `System.Text.Json` deserialization

## AppHost Changes

The current `Schleusenwerk.AppHost/Program.cs` uses `AddProject<T>()` for the proxy. For Docker-based testing, the AppHost needs a second code path (or a separate test AppHost) that uses `AddDockerfile()` instead. 

Approach: Create a dedicated `IntegrationTestAppHost` class inside the test project that builds its own `DistributedApplication` using `AddDockerfile()`. This avoids modifying the existing AppHost (which is fine for dev with `AddProject`).

## Out of Scope

- Testing ACME/Let's Encrypt (requires real domain + DNS)
- Testing Docker Discovery (requires Docker-in-Docker)
- Certificate upload tests (would need test cert files)
- Performance/load testing
