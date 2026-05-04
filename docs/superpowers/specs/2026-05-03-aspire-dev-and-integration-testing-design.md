# Aspire Dev Environment & Integration Testing — Design Spec

## Overview

Add a .NET Aspire orchestration layer to Schleusenwerk for local development and automated integration testing. All components run as containers via Podman. The setup validates the gRPC management API end-to-end against real containers without mocking.

## Architecture

```
Schleusenwerk.AppHost (Aspire Orchestrator, runs on host)
│
├── proxy (Container, existing Dockerfile)
│   ├── Ports: 80 (HTTP), 443 (HTTPS), 5000 (gRPC/HTTP2)
│   ├── Volume: proxy-data → /data (dev) or in-memory SQLite (test)
│   └── Depends on: upstream-mock
│
├── ui (Container, existing Dockerfile)
│   ├── Port: 8080
│   ├── Env: PROXY_GRPC_ENDPOINT → proxy:5000
│   └── Depends on: proxy
│
└── upstream-mock (Container, nginx:alpine)
    ├── Port: 80
    └── Returns static JSON on all requests
```

### Containers

- **proxy** — Existing `src/Schleusenwerk/Dockerfile`. No code changes. Aspire injects environment variables and manages lifecycle.
- **ui** — Existing `src/Schleusenwerk.UI/Dockerfile`. `PROXY_GRPC_ENDPOINT` injected via Aspire endpoint reference.
- **upstream-mock** — Stock `nginx:alpine` image with a custom `nginx.conf` that returns `200 {"status":"ok","server":"upstream-mock"}` on all requests. Used as a target for proxy route forwarding tests.

## New Projects

### Schleusenwerk.AppHost

Aspire host project. Orchestrates all containers.

```
src/Schleusenwerk.AppHost/
    Schleusenwerk.AppHost.csproj      Aspire AppHost SDK
    Program.cs                        Container orchestration
    nginx.conf                        Upstream mock config
    appsettings.json                  Aspire dashboard config
```

#### Program.cs

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var nginx = builder.AddContainer("upstream-mock", "nginx", "alpine")
    .WithBindMount("./nginx.conf", "/etc/nginx/nginx.conf", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 80, name: "http");

var proxy = builder.AddDockerfile("proxy", "../Schleusenwerk")
    .WithHttpEndpoint(targetPort: 80, name: "http")
    .WithHttpEndpoint(targetPort: 5000, name: "grpc")
    .WithVolume("proxy-data", "/data")
    .WaitFor(nginx);

builder.AddDockerfile("ui", "../Schleusenwerk.UI")
    .WithHttpEndpoint(targetPort: 8080, name: "http")
    .WithEnvironment("PROXY_GRPC_ENDPOINT", proxy.GetEndpoint("grpc"))
    .WaitFor(proxy);

builder.Build().Run();
```

#### nginx.conf

```nginx
events {}
http {
    server {
        listen 80;
        location / {
            return 200 '{"status":"ok","server":"upstream-mock"}';
            add_header Content-Type application/json;
        }
        location /health {
            return 200 'ok';
        }
    }
}
```

### Schleusenwerk.ServiceDefaults

Shared extensions for OpenTelemetry, health checks, and resilience. Referenced by AppHost.

```
src/Schleusenwerk.ServiceDefaults/
    Schleusenwerk.ServiceDefaults.csproj
    Extensions.cs
```

Standard Aspire ServiceDefaults template with OpenTelemetry tracing/metrics, health check endpoints, and HTTP resilience defaults.

### Schleusenwerk.IntegrationTests

xUnit v3 test project using `Aspire.Hosting.Testing` to spin up the full stack programmatically.

```
src/Schleusenwerk.IntegrationTests/
    Schleusenwerk.IntegrationTests.csproj
    SchleusenwerkFixture.cs           Shared fixture: starts AppHost, exposes gRPC channel + HTTP client
    RouteManagementSpec.cs            Route CRUD integration tests
    CertificateManagementSpec.cs      Certificate listing tests
    UpstreamForwardingSpec.cs         End-to-end proxy forwarding tests
    EventStreamSpec.cs                gRPC event stream tests
```

#### SchleusenwerkFixture

Shared fixture that starts the Aspire AppHost once per test class collection. Provides:
- `GrpcChannel` pointing at the proxy's gRPC endpoint (port 5000)
- `HttpClient` pointing at the proxy's HTTP endpoint (port 80) for forwarding tests
- `HttpClient` pointing at the UI's HTTP endpoint (port 8080) for smoke tests

```csharp
public sealed class SchleusenwerkFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public GrpcChannel GrpcChannel { get; private set; } = null!;
    public HttpClient ProxyHttp { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Schleusenwerk_AppHost>();

        App = await builder.BuildAsync();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await App.StartAsync(cts.Token);

        var grpcEndpoint = App.GetEndpoint("proxy", "grpc");
        GrpcChannel = GrpcChannel.ForAddress(grpcEndpoint.AbsoluteUri);

        var httpEndpoint = App.GetEndpoint("proxy", "http");
        ProxyHttp = new HttpClient { BaseAddress = httpEndpoint };
    }

    public async Task DisposeAsync()
    {
        GrpcChannel.Dispose();
        ProxyHttp.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
```

#### Test Scenarios

**RouteManagementSpec** — gRPC route CRUD:
- `AddRoute_then_ListRoutes_should_contain_new_route` — Add a route via gRPC, list routes, verify it appears
- `AddRoute_then_GetRoute_should_return_detail` — Add a route, get detail, verify config matches
- `UpdateRoute_should_change_config` — Add route, update ForceHttps, verify change persisted
- `DeleteRoute_should_remove_from_list` — Add route, delete, verify list is empty
- `AddUpstream_then_GetRoute_should_include_upstream` — Add route + upstream, verify upstream in detail
- `RemoveUpstream_should_remove_from_route` — Add upstream, remove it, verify gone

**CertificateManagementSpec** — gRPC certificate queries:
- `ListCertificates_should_return_provisioned_certs` — After route is added and cert is auto-provisioned, list should include it
- `ProvisionCertificate_should_trigger_generation` — Call provision, then list, verify cert appears

**UpstreamForwardingSpec** — HTTP end-to-end forwarding:
- `Request_to_configured_domain_should_forward_to_upstream` — Add route with nginx-mock upstream via gRPC, send HTTP request to proxy with matching Host header, verify response contains nginx-mock JSON body
- `Request_to_unknown_domain_should_return_error` — Send HTTP request with unknown Host header, verify error response

**EventStreamSpec** — gRPC server-side streaming:
- `Subscribe_should_receive_route_added_event` — Start Subscribe stream, add a route via separate gRPC call, verify RouteUpdated event arrives on stream

## Podman Configuration

Aspire uses the Docker CLI by default. Podman compatibility requires:

1. Podman machine must be running (already the case)
2. Set `DOCKER_HOST` environment variable to point at the Podman socket, OR
3. Configure the Podman Docker-compatible socket so `docker` CLI commands are routed to Podman

No code changes needed — this is a host-level configuration. The AppHost `launchSettings.json` can include the `DOCKER_HOST` env var for convenience.

## SQLite Persistence Strategy

- **Dev (manual `dotnet run`):** Volume mount `proxy-data → /data`, SQLite persists across restarts
- **Integration tests:** The proxy container gets `Akka__Persistence__ConnectionString=Data Source=:memory:;Cache=Shared` via environment variable, so each test run starts with clean state. The existing `appsettings.json` in the proxy already reads this config key.

## Package Versions

Add to `Directory.Packages.props`:

```xml
<ItemGroup Label="Aspire">
    <PackageVersion Include="Aspire.Hosting" Version="9.3.0" />
    <PackageVersion Include="Aspire.Hosting.Testing" Version="9.3.0" />
    <PackageVersion Include="Aspire.Hosting.AppHost" Version="9.3.0" />
</ItemGroup>
```

## What Does NOT Change

- **Proxy code** — No modifications. Container runs as-is.
- **UI code** — No modifications. Container runs as-is.
- **Existing unit tests** — Unaffected. The 237 existing tests continue to run independently.
- **docker-compose.yml** — Kept as alternative. Aspire does not replace it; both can coexist.
- **Existing Dockerfiles** — Used directly by Aspire's `AddDockerfile`.

## Error Handling

- **Container startup timeout:** `SchleusenwerkFixture` uses a 3-minute CancellationToken. If containers don't start, tests fail with a clear timeout error.
- **Proxy not ready:** Tests wait for the proxy's `/health` endpoint to return 200 before running gRPC calls.
- **Nginx mock not ready:** Aspire `WaitFor(nginx)` ensures the proxy doesn't start until nginx is healthy.
