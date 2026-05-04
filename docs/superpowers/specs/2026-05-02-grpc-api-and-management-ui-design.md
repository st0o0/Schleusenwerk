# gRPC Management API & Blazor Management UI — Design Spec

## Overview

Add a gRPC management API to the proxy container and a Blazor Server management UI as a separate container. The UI communicates exclusively via gRPC with the proxy for CRUD operations and real-time event streaming.

## Architecture

```
+---------------------+        gRPC (Port 5000)        +----------------------+
|   Proxy Container   |<------------------------------>|    UI Container      |
|                     |   Request/Response + Streaming   |                      |
|  Kestrel (80/443)   |                                 |  Blazor Server       |
|  Akka Actor System  |                                 |  MudBlazor           |
|  gRPC Services      |                                 |  gRPC Client         |
|  EventHub ----------|-- gRPC Stream ----------------->|  BackgroundService   |
|                     |                                 |       |              |
|                     |                                 |  SignalR Hub --------|--> Browser
+---------------------+                                 +----------------------+
```

### Containers

- **Proxy** (existing): Adds gRPC services on port 5000 (HTTP/2, no TLS inside Docker network). Port 80/443 continues to serve proxy traffic.
- **UI** (existing skeleton): Blazor Server with MudBlazor, gRPC client stubs, SignalR event hub.

### Shared Project

New `Schleusenwerk.Contracts` project contains `.proto` files. Referenced by both containers:
- `Schleusenwerk` references with `GrpcServices="Server"` (generates server stubs)
- `Schleusenwerk.UI` references with `GrpcServices="Client"` (generates client stubs)
- `Schleusenwerk.Contracts` itself uses `GrpcServices="None"` (message types only)

## gRPC Service Definitions

Four proto files in `Schleusenwerk.Contracts/Protos/`.

### routes.proto — RouteService

| RPC | Request | Response | Description |
|-----|---------|----------|-------------|
| ListRoutes | Empty | ListRoutesResponse | All domains with upstreams and source (manual/docker) |
| GetRoute | GetRouteRequest (domain) | RouteDetail | Config + upstreams + health status |
| AddRoute | AddRouteRequest (domain, force_https, timeout) | CommandResult | Create new manual route |
| UpdateRoute | UpdateRouteRequest (domain, force_https, timeout) | CommandResult | Update existing route config |
| DeleteRoute | DeleteRouteRequest (domain) | CommandResult | Remove a route |
| AddUpstream | AddUpstreamRequest (domain, url, weight) | CommandResult | Add upstream to route |
| RemoveUpstream | RemoveUpstreamRequest (domain, url) | CommandResult | Remove upstream from route |

### certificates.proto — CertificateService

| RPC | Request | Response | Description |
|-----|---------|----------|-------------|
| ListCertificates | Empty | ListCertificatesResponse | All domains with cert metadata |
| GetCertificate | GetCertificateRequest (domain) | CertificateDetail | Thumbprint, NotBefore, NotAfter, IsSelfSigned |
| ProvisionCertificate | ProvisionCertificateRequest (domain) | CommandResult | Trigger manual cert generation |

### health.proto — HealthService

| RPC | Request | Response | Description |
|-----|---------|----------|-------------|
| GetHealth | Empty | ProxyHealthResponse | Overall proxy status: route count, healthy count, unhealthy count (no uptime tracking for MVP) |
| GetUpstreamHealth | GetUpstreamHealthRequest (domain) | UpstreamHealthResponse | Health status of all upstreams for a domain |

### events.proto — EventService

| RPC | Request | Response | Description |
|-----|---------|----------|-------------|
| Subscribe | SubscribeRequest (optional filter) | server-side stream of ProxyEvent | Real-time event stream |

ProxyEvent types: `RouteAdded`, `RouteRemoved`, `RouteUpdated`, `UpstreamHealthChanged`, `CertificateProvisioned`, `CertificateExpiring`.

### Shared Messages

`CommandResult`: `{ bool success, string error_message }`

## Proxy-Side gRPC Implementation

gRPC services live in `Schleusenwerk/Grpc/`. Each service delegates to existing infrastructure:

- **RouteServiceImpl** — delegates to `IConfigurationService` (1:1 mapping). Protobuf ↔ domain type mapping in the service class.
- **CertificateServiceImpl** — reads from `ICertificateStore` for queries, sends `CertificateProvisioningRequested` to `EventHub` for provisioning.
- **HealthServiceImpl** — reads domain list from `IConfigurationStore`, Ask `DomainEntityActor` with new `GetHealthStatus` command for per-upstream health state. This requires adding a `GetHealthStatus` command to `DomainEntityActor` that returns the list of upstreams with their healthy/unhealthy status (the actor already tracks `_unhealthyUrls` internally).
- **EventServiceImpl** — subscribes to `EventHub` via `EventHub.Subscribe.Instance`, maps `IClusterEvent` to Protobuf `ProxyEvent`, writes to `IServerStreamWriter<ProxyEvent>`.

### Kestrel Configuration

Two endpoints in `SchleusenwerkApplicationSetup`:

```csharp
options.Listen(IPAddress.Any, 80);
options.Listen(IPAddress.Any, 443, o => o.UseHttps(...));
options.Listen(IPAddress.Any, 5000, o => o.Protocols = HttpProtocols.Http2);
```

gRPC services mapped via `app.MapGrpcService<T>()`.

## UI-Side Architecture

### Dependencies

- `Grpc.Net.Client` — gRPC client
- `MudBlazor` — component library
- `Microsoft.AspNetCore.SignalR.Client` — for Blazor components to connect to the local hub
- `Schleusenwerk.Contracts` — shared proto types
- `Schleusenwerk.Core` — domain types (DomainName, DomainConfig, etc.)

### Service Layer

Thin wrapper interfaces around generated gRPC clients. These map Protobuf types to domain types from `Schleusenwerk.Core` so Blazor pages never touch Protobuf directly.

- `IRouteClient` — wraps `RouteService.RouteServiceClient`
- `ICertificateClient` — wraps `CertificateService.CertificateServiceClient`
- `IHealthClient` — wraps `HealthService.HealthServiceClient`

Registered as singletons in DI. The gRPC channel is configured from `PROXY_GRPC_ENDPOINT` environment variable (set in docker-compose as `http://proxy:5000`).

### Event Bridge

1. **EventStreamBackgroundService** (IHostedService) — opens `EventService.Subscribe` gRPC stream on startup. Reconnects with exponential backoff on disconnect.
2. Received `ProxyEvent` messages are pushed to `ProxyEventHub` via `IHubContext<ProxyEventHub>`.
3. **ProxyEventHub** (SignalR Hub at `/hubs/events`) — Blazor components connect and subscribe to event types.
4. Pages call `InvokeAsync(StateHasChanged)` when relevant events arrive.

### Pages

| Route | Page | Components |
|-------|------|-----------|
| `/` | Dashboard | MudCard grid: route count, healthy/unhealthy count, cert expiry warnings |
| `/routes` | Route List | MudTable: all routes, source badge (manual/docker), health indicator dot, edit/delete actions |
| `/routes/new` | Add Route | MudForm: domain input, ForceHttps switch, timeout slider, first upstream URL |
| `/routes/{domain}` | Route Detail | Editable config section, MudTable of upstreams with add/remove, health status per upstream |
| `/certificates` | Certificate List | MudTable: domain, thumbprint (truncated), expiry date, self-signed badge, "Renew" action button |

### Layout

MudBlazor `MudLayout` with:
- `MudAppBar` — "Schleusenwerk" title
- `MudDrawer` — navigation: Dashboard, Routes, Certificates
- `MudMainContent` — page content

## Project Structure

```
src/
  Schleusenwerk.Contracts/
    Schleusenwerk.Contracts.csproj
    Protos/
      routes.proto
      certificates.proto
      health.proto
      events.proto

  Schleusenwerk/                        (existing, modified)
    Grpc/
      RouteServiceImpl.cs
      CertificateServiceImpl.cs
      HealthServiceImpl.cs
      EventServiceImpl.cs
      ProtoMapper.cs                    (Protobuf <-> domain type conversions)
    Startup/
      SchleusenwerkApplicationSetup.cs  (modified: add gRPC mapping + port 5000)
      SchleusenwerkServicesSetup.cs     (modified: add gRPC services)

  Schleusenwerk.UI/                     (existing, rewritten)
    Program.cs
    Hubs/
      ProxyEventHub.cs
    Services/
      IRouteClient.cs
      RouteClient.cs
      ICertificateClient.cs
      CertificateClient.cs
      IHealthClient.cs
      HealthClient.cs
      EventStreamBackgroundService.cs
    Components/
      Layout/
        MainLayout.razor
        NavMenu.razor
      Pages/
        Dashboard.razor
        RouteList.razor
        RouteCreate.razor
        RouteDetail.razor
        CertificateList.razor
      Shared/
        HealthIndicator.razor           (colored dot: green/yellow/red)
        SourceBadge.razor               (manual/docker badge)
```

## Error Handling

- gRPC services return `CommandResult { success, error_message }` — no gRPC status code exceptions for business errors.
- UI service layer maps `CommandResult` failures to `Snackbar` notifications via MudBlazor's `ISnackbar`.
- gRPC transport errors (proxy unreachable) shown as a persistent error banner in the layout.
- Event stream reconnection handled silently with exponential backoff; a "disconnected" indicator shows in the AppBar when the stream is down.

## Testing

- **Proxy gRPC services:** Unit tests with mocked `IConfigurationService`, `ICertificateStore`, `IConfigurationStore`. No actual gRPC transport — test the service classes directly.
- **UI service layer:** Unit tests with mocked gRPC clients, verifying Protobuf ↔ domain mapping.
- **No Blazor component tests for MVP** — manual testing via browser is sufficient for the initial UI.
