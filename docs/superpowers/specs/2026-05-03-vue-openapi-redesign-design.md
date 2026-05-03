# Schleusenwerk UI Redesign — Vue.js + OpenAPI

## Summary

Replace the Blazor Server frontend and gRPC communication layer with a Vue.js SPA and a contract-first OpenAPI REST API. The `openapi.yaml` is the single source of truth, from which NSwag generates both C# base controllers (backend) and a TypeScript client (frontend). Live events continue via SignalR WebSocket. The Blueprint theme design (colors, Schleusen-Terminologie, pages) carries over unchanged.

## Architecture

```
+---------------------+       REST/JSON (Port 5000)      +----------------------+
|   Proxy Container   |<-------------------------------->|   Web Container      |
|                     |                                   |                      |
|  Kestrel (80/443)   |   GET/POST/PUT/DELETE             |  nginx               |
|  Akka Actor System  |   (OpenAPI-generated Controllers) |  Vue 3 SPA           |
|  ASP.NET Controllers|                                   |  PrimeVue (Unstyled) |
|  SignalR Hub -------|-- WebSocket (/hubs/events) ------>|  Pinia Store          |
+---------------------+                                   +----------------------+

Shared Contract:
  openapi.yaml (single source of truth)
    +-- NSwag --> C# Base Controllers (in Schleusenwerk/)
    +-- NSwag --> TypeScript Client   (in Schleusenwerk.Web/)
```

### Core Principle

The `openapi.yaml` is the contract. NSwag generates:
- **Backend:** Abstract C# controller classes implemented in the Schleusenwerk project
- **Frontend:** TypeScript API client consumed by Vue components through Pinia stores

SignalR remains for live events (Flussprotokoll). It is a separate channel alongside the REST API and is not defined in OpenAPI. The Vue client connects directly via the `@microsoft/signalr` npm package.

gRPC is removed entirely — proto files, Contracts project gRPC tooling, and gRPC service implementations are replaced by REST controllers.

## OpenAPI Contract

The `openapi.yaml` lives in `src/Schleusenwerk.Contracts/`. The project is converted from Proto/gRPC to OpenAPI. It remains the "contract project" for both sides.

### API Endpoints

| Area | Method | Path | Description |
|------|--------|------|-------------|
| Routes | GET | `/api/routes` | List all routes |
| | GET | `/api/routes/{domain}` | Route detail with health |
| | POST | `/api/routes` | Create new route |
| | PUT | `/api/routes/{domain}` | Update route config |
| | DELETE | `/api/routes/{domain}` | Delete route |
| | POST | `/api/routes/{domain}/upstreams` | Add upstream |
| | DELETE | `/api/routes/{domain}/upstreams/{url}` | Remove upstream (url is base64url-encoded) |
| Certificates | GET | `/api/certificates` | List all certificates |
| | GET | `/api/certificates/{domain}` | Certificate detail |
| | POST | `/api/certificates/{domain}/provision` | Trigger certificate renewal |
| Health | GET | `/api/health` | Overall proxy status |
| | GET | `/api/health/{domain}` | Upstream health per domain |

### Response Models

Content-wise identical to the existing Proto messages, serialized as JSON instead of Protobuf. `CommandResult { success, errorMessage }` remains the standard response format for mutations.

### NSwag Workflow

1. `openapi.yaml` is maintained manually (contract-first)
2. `nswag run` during build generates:
   - `src/Schleusenwerk/Controllers/Generated/` — abstract controller classes
   - `src/Schleusenwerk.Web/src/api/generated/` — TypeScript client + models
3. Implementation controllers inherit from generated base controllers and delegate to existing services (`IConfigurationService`, `ICertificateStore`, etc.)

## Backend — Controller Implementation

Generated base controllers are abstract. Concrete implementations live in `src/Schleusenwerk/Controllers/` and delegate to existing infrastructure — exactly as the current gRPC services do.

### Structure

```
src/Schleusenwerk/
  Controllers/
    Generated/                  <-- NSwag-generated, do not edit
      RouteControllerBase.cs
      CertificateControllerBase.cs
      HealthControllerBase.cs
    RouteController.cs          <-- inherits RouteControllerBase, delegates to IConfigurationService
    CertificateController.cs    <-- inherits, delegates to ICertificateStore
    HealthController.cs         <-- inherits, delegates to IConfigurationStore + Actor Ask
  Hubs/
    ProxyEventHub.cs            <-- SignalR Hub (stays, freed from Blazor dependencies)
```

### Migration

The existing `Grpc/*Impl.cs` classes are migrated 1:1 to controllers. Logic stays identical — only Protobuf mapping is replaced by JSON serialization (automatic via ASP.NET).

`ProtoMapper.cs` is removed — the REST models from the OpenAPI spec are generated directly as C# DTOs, no manual mapping needed.

### Kestrel Configuration

- Port 80/443 — Proxy traffic (unchanged)
- Port 5000 — Management API (REST instead of gRPC, HTTP/1.1+JSON instead of HTTP/2+Protobuf)
- SignalR Hub at `/hubs/events` on port 5000

## Frontend — Vue.js Project

### Project: `src/Schleusenwerk.Web/`

```
src/Schleusenwerk.Web/
  package.json
  vite.config.ts
  tsconfig.json
  index.html
  Dockerfile                        <-- Multi-stage: npm build -> nginx
  src/
    main.ts                         <-- App bootstrap, Router, Pinia, SignalR
    App.vue                         <-- Blueprint-Grid + Schaltleiste + RouterView
    router/
      index.ts                      <-- Vue Router configuration
    api/
      generated/                    <-- NSwag-generated, do not edit
        client.ts                   <-- TypeScript API client
        models.ts                   <-- DTOs (RouteSummary, CertificateSummary, etc.)
      signalr.ts                    <-- SignalR Connection Manager (reconnect, typed events)
    stores/
      routes.ts                     <-- Pinia Store: CRUD Routes, Upstreams
      certificates.ts               <-- Pinia Store: List, Provision
      health.ts                     <-- Pinia Store: Proxy status
      events.ts                     <-- Pinia Store: Live events via SignalR
    components/
      SchaltleisteNav.vue           <-- Top navigation
      StatusIndicator.vue           <-- Square-dot status (offen/gesperrt/warnung)
      QuelleBadge.vue               <-- docker/manuell badge
      SiegelIcon.vue                <-- Certificate status diamond
      BpPanel.vue                   <-- Blueprint panel wrapper
      BpKpi.vue                     <-- KPI card
    pages/
      Leitstand.vue                 <-- Dashboard (/)
      Schleusentore.vue             <-- Route List (/tore)
      TorEinsetzen.vue              <-- Route Create (/tore/neu)
      TorDetail.vue                 <-- Route Detail (/tore/:domain)
      Siegel.vue                    <-- Certificates (/siegel)
      Flussprotokoll.vue            <-- Live Events (/flussprotokoll)
      Hafenbecken.vue               <-- Docker Discovery (/hafenbecken)
      Stellwerk.vue                 <-- Settings (/stellwerk)
    assets/
      blueprint.css                 <-- Blueprint theme (carried over + adapted)
      primevue-passthrough.ts       <-- PrimeVue Unstyled PT config with Blueprint classes
```

### Tech Stack

- Vue 3 + Vite + TypeScript
- Pinia for state management
- Vue Router for navigation
- PrimeVue in Unstyled mode (headless components with Blueprint CSS via Passthrough API)
- `@microsoft/signalr` for WebSocket event stream

### PrimeVue Unstyled

The Passthrough API maps Blueprint CSS classes directly onto PrimeVue components (`DataTable` gets `bp-panel` + monospace styles, `InputText` gets Blueprint input styles, etc.). No PrimeVue theme — only headless components with custom styling.

### Pinia Stores

Stores encapsulate all API calls through the generated client and hold state. Pages inject only stores, never the API client directly.

### SignalR

Dedicated connection manager in `signalr.ts` with `@microsoft/signalr`, automatic reconnect, and typed events. The `events` store subscribes to the connection and pushes events reactively into Vue components.

## Deployment

### Docker Compose

```yaml
# docker-compose.yml
services:
  proxy:
    build: src/Schleusenwerk/
    ports:
      - "80:80"
      - "443:443"
      - "5000:5000"         # Management API + SignalR

  web:
    build: src/Schleusenwerk.Web/
    ports:
      - "3000:80"           # nginx serving SPA
    environment:
      - API_URL=http://proxy:5000
```

### Web Container Dockerfile

Multi-stage build:
1. `node:22-alpine` — `npm ci && npm run build` (Vite produces static files)
2. `nginx:alpine` — copies `dist/` to `/usr/share/nginx/html/`, nginx config with proxy_pass for `/api/*` and `/hubs/*` to `http://proxy:5000`

### nginx Configuration

- `/` — SPA (index.html fallback for Vue Router history mode)
- `/api/*` — `proxy_pass http://proxy:5000`
- `/hubs/*` — `proxy_pass http://proxy:5000` (with WebSocket upgrade headers)

The browser only talks to the web container. CORS is not an issue because nginx as reverse proxy brings everything onto the same origin.

### Aspire AppHost

`Schleusenwerk.UI` reference is replaced by a container resource for `Schleusenwerk.Web`.

### Dev Workflow

- `npm run dev` in `Schleusenwerk.Web/` starts Vite dev server on port 5173
- Vite proxy in `vite.config.ts` forwards `/api/*` and `/hubs/*` to `http://localhost:5000`
- Backend runs in parallel with `dotnet run`

## What Changes

### Removed Entirely

- `src/Schleusenwerk.UI/` — entire Blazor project
- `src/Schleusenwerk.Contracts/Protos/*.proto` — all 5 proto files
- `src/Schleusenwerk/Grpc/` — all 5 gRPC implementations including ProtoMapper
- gRPC NuGet packages (Grpc.AspNetCore, Grpc.Tools, Google.Protobuf) from proxy + contracts
- MudBlazor, Blazor SignalR client packages

### Unchanged

- All actor infrastructure (DomainRouterActor, HealthCheckActor, etc.)
- Services (IConfigurationService, ICertificateStore, IConfigurationStore)
- Persistence (Akka.Persistence event-sourced)
- Proxy core (Kestrel edge, request pipeline)
- Docker discovery
- Blueprint design (colors, terminology, page structure, CSS variables)
- `Schleusenwerk.Core` — domain types
- `Schleusenwerk.Tests` — existing tests

### Modified

- `Schleusenwerk.Contracts` — from Proto/gRPC to OpenAPI YAML + NSwag
- `Schleusenwerk.csproj` — gRPC packages out, NSwag + ASP.NET controllers in
- `Schleusenwerk.AppHost` — Blazor UI reference out, Web container reference in
- Kestrel port 5000 — from HTTP/2 gRPC to HTTP/1.1 REST+JSON
- SignalR Hub stays but Blazor-specific dependencies are removed
- `docker-compose.yml` — UI container becomes Web container with nginx

## Design Decisions

| Decision | Chosen | Alternatives Considered | Rationale |
|----------|--------|------------------------|-----------|
| Frontend framework | Vue 3 + Vite + TypeScript | Blazor Server (current) | Cleaner separation, better frontend ecosystem, no .NET dependency in UI |
| API protocol | REST + OpenAPI (contract-first) | gRPC (current) | Browser-native, tooling for client/server generation, easier debugging |
| Code generation | NSwag | OpenAPI Generator, Kiota | Single tool for both C# controllers and TypeScript client, good .NET integration |
| Component library | PrimeVue (Unstyled) | Vuetify, no library | Headless components with Passthrough API let Blueprint CSS dominate without fighting framework styles |
| State management | Pinia | Vuex, composables-only | Vue 3 standard, TypeScript-first, simpler than Vuex |
| Live events | SignalR (WebSocket) | SSE, Polling | Bidirectional, existing SignalR Hub can be reused, `@microsoft/signalr` npm package available |
| Deployment | Separate containers (nginx + proxy) | SPA embedded in proxy | Clean separation of concerns, independent scaling, standard nginx SPA hosting |

## Out of Scope

- Authentication/authorization for the management UI
- Dark/light theme toggle (Blueprint is dark-only by design)
- Mobile responsive layout (desktop DevOps tool)
- Internationalization (German Schleusen-terms are the identity, not i18n)
- Real-time metrics/charts (KPIs are point-in-time, Flussprotokoll is the live view)
