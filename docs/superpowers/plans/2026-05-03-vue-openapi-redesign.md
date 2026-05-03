# Schleusenwerk Vue.js + OpenAPI Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Blazor Server + gRPC with a Vue.js SPA frontend and contract-first OpenAPI REST API, keeping the Blueprint theme and Schleusen-Terminologie.

**Architecture:** `openapi.yaml` is the single source of truth. NSwag generates abstract C# base controllers (backend) and a TypeScript API client (frontend). The Vue.js SPA lives in `src/Schleusenwerk.Web/`, served by nginx. SignalR WebSocket remains for live events. The proxy container exposes REST+JSON on port 5000 instead of gRPC.

**Tech Stack:** ASP.NET Core Controllers, NSwag, Vue 3 + Vite + TypeScript, PrimeVue (Unstyled), Pinia, Vue Router, @microsoft/signalr, nginx

**Spec:** `docs/superpowers/specs/2026-05-03-vue-openapi-redesign-design.md`

---

## File Map

### New files
- `src/Schleusenwerk.Contracts/openapi.yaml` — OpenAPI 3.0 contract definition
- `src/Schleusenwerk.Contracts/nswag.json` — NSwag configuration for C# controller generation
- `src/Schleusenwerk/Controllers/Generated/ControllersBase.cs` — NSwag-generated abstract controllers (generated, do not edit)
- `src/Schleusenwerk/Controllers/RouteController.cs` — Route API implementation
- `src/Schleusenwerk/Controllers/CertificateController.cs` — Certificate API implementation
- `src/Schleusenwerk/Controllers/HealthController.cs` — Health API implementation
- `src/Schleusenwerk/Hubs/ProxyEventHub.cs` — SignalR hub (moved from UI project)
- `src/Schleusenwerk/Hubs/EventBridgeService.cs` — Bridges Akka EventHub to SignalR
- `src/Schleusenwerk/Api/ApiModels.cs` — JSON DTOs for REST API responses
- `src/Schleusenwerk/Api/DomainModelMapper.cs` — Maps domain types to API DTOs
- `src/Schleusenwerk.Web/` — entire Vue.js SPA project (detailed in Task 8+)

### Modified files
- `src/Schleusenwerk/Schleusenwerk.csproj` — remove gRPC, add controllers + SignalR + NSwag
- `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs` — replace gRPC with controllers + SignalR
- `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs` — replace gRPC mappings with controller + hub mappings
- `src/Schleusenwerk.Contracts/Schleusenwerk.Contracts.csproj` — remove Proto/gRPC, keep as contract project
- `src/Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj` — remove UI project reference
- `src/Schleusenwerk.AppHost/Program.cs` — replace UI with web container
- `src/Schleusenwerk.slnx` — remove UI project, optionally add Web
- `src/Directory.Packages.props` — remove gRPC/MudBlazor packages, add NSwag
- `docker-compose.yml` — replace UI container with Web container

### Deleted files/directories
- `src/Schleusenwerk/Grpc/` — entire directory (RouteServiceImpl.cs, CertificateServiceImpl.cs, HealthServiceImpl.cs, EventServiceImpl.cs, ProtoMapper.cs)
- `src/Schleusenwerk.Contracts/Protos/` — entire directory (routes.proto, certificates.proto, health.proto, events.proto, common.proto)
- `src/Schleusenwerk.UI/` — entire project

---

## Task 1: Write OpenAPI Contract

**Files:**
- Create: `src/Schleusenwerk.Contracts/openapi.yaml`

- [ ] **Step 1: Create the OpenAPI 3.0 specification**

```yaml
# src/Schleusenwerk.Contracts/openapi.yaml
openapi: '3.0.3'
info:
  title: Schleusenwerk Management API
  version: '1.0.0'
  description: REST API for managing the Schleusenwerk reverse proxy

paths:
  /api/routes:
    get:
      operationId: listRoutes
      tags: [Routes]
      summary: List all configured routes
      responses:
        '200':
          description: List of route summaries
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/RouteSummary'
    post:
      operationId: addRoute
      tags: [Routes]
      summary: Create a new route
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AddRouteRequest'
      responses:
        '200':
          description: Command result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommandResult'

  /api/routes/{domain}:
    parameters:
      - name: domain
        in: path
        required: true
        schema:
          type: string
    get:
      operationId: getRoute
      tags: [Routes]
      summary: Get route detail with health status
      responses:
        '200':
          description: Route detail
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RouteDetail'
        '404':
          description: Route not found
    put:
      operationId: updateRoute
      tags: [Routes]
      summary: Update route configuration
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateRouteRequest'
      responses:
        '200':
          description: Command result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommandResult'
    delete:
      operationId: deleteRoute
      tags: [Routes]
      summary: Delete a route
      responses:
        '200':
          description: Command result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommandResult'

  /api/routes/{domain}/upstreams:
    parameters:
      - name: domain
        in: path
        required: true
        schema:
          type: string
    post:
      operationId: addUpstream
      tags: [Routes]
      summary: Add an upstream to a route
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AddUpstreamRequest'
      responses:
        '200':
          description: Command result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommandResult'

  /api/routes/{domain}/upstreams/{encodedUrl}:
    parameters:
      - name: domain
        in: path
        required: true
        schema:
          type: string
      - name: encodedUrl
        in: path
        required: true
        schema:
          type: string
        description: Base64url-encoded upstream URL
    delete:
      operationId: removeUpstream
      tags: [Routes]
      summary: Remove an upstream from a route
      responses:
        '200':
          description: Command result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommandResult'

  /api/certificates:
    get:
      operationId: listCertificates
      tags: [Certificates]
      summary: List all certificates
      responses:
        '200':
          description: List of certificate summaries
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/CertificateSummary'

  /api/certificates/{domain}:
    parameters:
      - name: domain
        in: path
        required: true
        schema:
          type: string
    get:
      operationId: getCertificate
      tags: [Certificates]
      summary: Get certificate detail
      responses:
        '200':
          description: Certificate detail
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CertificateDetail'
        '404':
          description: Certificate not found

  /api/certificates/{domain}/provision:
    parameters:
      - name: domain
        in: path
        required: true
        schema:
          type: string
    post:
      operationId: provisionCertificate
      tags: [Certificates]
      summary: Trigger certificate provisioning
      responses:
        '200':
          description: Command result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommandResult'

  /api/health:
    get:
      operationId: getHealth
      tags: [Health]
      summary: Get overall proxy health
      responses:
        '200':
          description: Proxy health status
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ProxyHealthResponse'

  /api/health/{domain}:
    parameters:
      - name: domain
        in: path
        required: true
        schema:
          type: string
    get:
      operationId: getUpstreamHealth
      tags: [Health]
      summary: Get upstream health for a domain
      responses:
        '200':
          description: Upstream health status
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/UpstreamHealthResponse'

components:
  schemas:
    CommandResult:
      type: object
      required: [success]
      properties:
        success:
          type: boolean
        errorMessage:
          type: string
          nullable: true

    RouteSummary:
      type: object
      required: [domain, forceHttps, source, timeoutSeconds, upstreams]
      properties:
        domain:
          type: string
        forceHttps:
          type: boolean
        source:
          type: string
        timeoutSeconds:
          type: integer
        upstreams:
          type: array
          items:
            $ref: '#/components/schemas/UpstreamInfo'

    UpstreamInfo:
      type: object
      required: [url, weight]
      properties:
        url:
          type: string
        weight:
          type: integer

    RouteDetail:
      type: object
      required: [domain, forceHttps, timeoutSeconds, source, upstreams, health]
      properties:
        domain:
          type: string
        forceHttps:
          type: boolean
        timeoutSeconds:
          type: integer
        source:
          type: string
        upstreams:
          type: array
          items:
            $ref: '#/components/schemas/UpstreamInfo'
        health:
          type: array
          items:
            $ref: '#/components/schemas/UpstreamHealthEntry'

    UpstreamHealthEntry:
      type: object
      required: [url, isHealthy]
      properties:
        url:
          type: string
        isHealthy:
          type: boolean

    AddRouteRequest:
      type: object
      required: [domain]
      properties:
        domain:
          type: string
        forceHttps:
          type: boolean
          default: false
        timeoutSeconds:
          type: integer
          default: 30
        firstUpstreamUrl:
          type: string
          nullable: true

    UpdateRouteRequest:
      type: object
      properties:
        forceHttps:
          type: boolean
        timeoutSeconds:
          type: integer

    AddUpstreamRequest:
      type: object
      required: [url]
      properties:
        url:
          type: string
        weight:
          type: integer
          default: 1

    CertificateSummary:
      type: object
      required: [domain, thumbprint, notAfter, isSelfSigned]
      properties:
        domain:
          type: string
        thumbprint:
          type: string
        notAfter:
          type: string
        isSelfSigned:
          type: boolean

    CertificateDetail:
      type: object
      required: [domain, thumbprint, notBefore, notAfter, issuer, isSelfSigned]
      properties:
        domain:
          type: string
        thumbprint:
          type: string
        notBefore:
          type: string
        notAfter:
          type: string
        issuer:
          type: string
        isSelfSigned:
          type: boolean

    ProxyHealthResponse:
      type: object
      required: [routeCount, healthyCount, unhealthyCount]
      properties:
        routeCount:
          type: integer
        healthyCount:
          type: integer
        unhealthyCount:
          type: integer

    UpstreamHealthResponse:
      type: object
      required: [domain, upstreams]
      properties:
        domain:
          type: string
        upstreams:
          type: array
          items:
            $ref: '#/components/schemas/UpstreamHealthEntry'

    ProxyEventDto:
      type: object
      required: [type, domain, message, isHealthy, upstreamUrl]
      properties:
        type:
          type: string
          enum: [RouteUpdated, RouteRemoved, UpstreamHealthChanged, CertificateProvisioned, CertificateExpiring]
        domain:
          type: string
        message:
          type: string
        isHealthy:
          type: boolean
        upstreamUrl:
          type: string
```

- [ ] **Step 2: Verify the YAML is valid**

Run: `npx @redocly/cli lint src/Schleusenwerk.Contracts/openapi.yaml` (or use any OpenAPI validator)
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.Contracts/openapi.yaml
git commit -m "feat(contracts): add OpenAPI 3.0 management API contract"
```

---

## Task 2: NSwag Configuration + C# Controller Generation

**Files:**
- Create: `src/Schleusenwerk.Contracts/nswag.json`
- Modify: `src/Schleusenwerk.Contracts/Schleusenwerk.Contracts.csproj`
- Modify: `src/Directory.Packages.props`

- [ ] **Step 1: Add NSwag.ApiDescription.Client package version to central package management**

Add inside the `<ItemGroup Label="UI">` (rename label to "API") in `src/Directory.Packages.props`:

Replace the existing UI ItemGroup:
```xml
    <ItemGroup Label="UI">
        <PackageVersion Include="MudBlazor" Version="8.3.1"/>
    </ItemGroup>
```

With:
```xml
    <ItemGroup Label="API">
        <PackageVersion Include="NSwag.ApiDescription.Client" Version="14.4.0"/>
        <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0"/>
    </ItemGroup>
```

- [ ] **Step 2: Create NSwag configuration for C# controller generation**

Create `src/Schleusenwerk.Contracts/nswag.json`:

```json
{
  "runtime": "Net90",
  "documentGenerator": {
    "fromDocument": {
      "url": "openapi.yaml",
      "output": null
    }
  },
  "codeGenerators": {
    "openApiToCSharpController": {
      "controllerStyle": "Abstract",
      "controllerTarget": "AspNetCore",
      "useCancellationToken": true,
      "routeNamingStrategy": "OperationId",
      "className": "{controller}ControllerBase",
      "namespace": "Schleusenwerk.Controllers.Generated",
      "generateOptionalParameters": true,
      "generateJsonMethods": false,
      "generateModelValidationAttributes": true,
      "output": "../Schleusenwerk/Controllers/Generated/ControllersBase.cs",
      "responseArrayType": "System.Collections.Generic.IReadOnlyList",
      "dateTimeType": "System.DateTimeOffset"
    },
    "openApiToTypeScriptClient": {
      "className": "SchleusenwerkClient",
      "moduleName": "",
      "namespace": "",
      "template": "Fetch",
      "promiseType": "Promise",
      "httpClass": "HttpClient",
      "useSingletonProvider": false,
      "generateClientClasses": true,
      "generateClientInterfaces": false,
      "generateOptionalParameters": true,
      "typeScriptVersion": 5.0,
      "output": "../Schleusenwerk.Web/src/api/generated/client.ts"
    }
  }
}
```

- [ ] **Step 3: Update Contracts csproj — remove gRPC, keep as contract-only project**

Replace the full content of `src/Schleusenwerk.Contracts/Schleusenwerk.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <ItemGroup>
        <None Include="openapi.yaml" />
        <None Include="nswag.json" />
    </ItemGroup>

</Project>
```

- [ ] **Step 4: Delete all proto files**

```bash
rm src/Schleusenwerk.Contracts/Protos/routes.proto
rm src/Schleusenwerk.Contracts/Protos/certificates.proto
rm src/Schleusenwerk.Contracts/Protos/health.proto
rm src/Schleusenwerk.Contracts/Protos/events.proto
rm src/Schleusenwerk.Contracts/Protos/common.proto
rmdir src/Schleusenwerk.Contracts/Protos
```

- [ ] **Step 5: Build contracts project to verify it compiles**

Run: `dotnet build src/Schleusenwerk.Contracts/Schleusenwerk.Contracts.csproj`
Expected: Build succeeds (project is now essentially empty — just holds the contract files).

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk.Contracts/ src/Directory.Packages.props
git rm -r src/Schleusenwerk.Contracts/Protos/
git commit -m "feat(contracts): replace gRPC protos with OpenAPI contract + NSwag config"
```

---

## Task 3: Generate C# Base Controllers + API Models

**Files:**
- Create: `src/Schleusenwerk/Controllers/Generated/ControllersBase.cs` (NSwag-generated)
- Create: `src/Schleusenwerk/Api/ApiModels.cs`
- Create: `src/Schleusenwerk/Api/DomainModelMapper.cs`
- Modify: `src/Schleusenwerk/Schleusenwerk.csproj`

- [ ] **Step 1: Install NSwag CLI tool locally**

Run: `dotnet tool install NSwag.ConsoleCore --version 14.4.0 --tool-path src/.tools`

- [ ] **Step 2: Generate the base controllers**

Run from `src/Schleusenwerk.Contracts/`:
```bash
cd src/Schleusenwerk.Contracts && ../tools/nswag run nswag.json
```

Expected: File created at `src/Schleusenwerk/Controllers/Generated/ControllersBase.cs` with abstract `RouteControllerBase`, `CertificateControllerBase`, `HealthControllerBase`.

If NSwag CLI isn't available or produces issues, the generated file can be written by hand following the OpenAPI spec. The key point: abstract controller classes with correct route attributes and method signatures.

- [ ] **Step 3: Create API model DTOs**

These are the JSON-serializable models used by the controllers. They mirror the OpenAPI schemas but are hand-written to stay independent of NSwag's model generation (simpler, uses records).

Create `src/Schleusenwerk/Api/ApiModels.cs`:

```csharp
namespace Schleusenwerk.Api;

public sealed record CommandResultDto(bool Success, string? ErrorMessage = null)
{
    public static CommandResultDto Ok() => new(true);
    public static CommandResultDto Fail(string reason) => new(false, reason);
}

public sealed record RouteSummaryDto(
    string Domain,
    bool ForceHttps,
    string Source,
    int TimeoutSeconds,
    IReadOnlyList<UpstreamInfoDto> Upstreams);

public sealed record UpstreamInfoDto(string Url, int Weight);

public sealed record RouteDetailDto(
    string Domain,
    bool ForceHttps,
    int TimeoutSeconds,
    string Source,
    IReadOnlyList<UpstreamInfoDto> Upstreams,
    IReadOnlyList<UpstreamHealthEntryDto> Health);

public sealed record UpstreamHealthEntryDto(string Url, bool IsHealthy);

public sealed record AddRouteRequestDto(
    string Domain,
    bool ForceHttps = false,
    int TimeoutSeconds = 30,
    string? FirstUpstreamUrl = null);

public sealed record UpdateRouteRequestDto(
    bool ForceHttps,
    int TimeoutSeconds);

public sealed record AddUpstreamRequestDto(
    string Url,
    int Weight = 1);

public sealed record CertificateSummaryDto(
    string Domain,
    string Thumbprint,
    string NotAfter,
    bool IsSelfSigned);

public sealed record CertificateDetailDto(
    string Domain,
    string Thumbprint,
    string NotBefore,
    string NotAfter,
    string Issuer,
    bool IsSelfSigned);

public sealed record ProxyHealthResponseDto(
    int RouteCount,
    int HealthyCount,
    int UnhealthyCount);

public sealed record UpstreamHealthResponseDto(
    string Domain,
    IReadOnlyList<UpstreamHealthEntryDto> Upstreams);

public sealed record ProxyEventDto(
    string Type,
    string Domain,
    string Message,
    bool IsHealthy,
    string UpstreamUrl);
```

- [ ] **Step 4: Create domain model mapper**

Create `src/Schleusenwerk/Api/DomainModelMapper.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Api;

internal static class DomainModelMapper
{
    public static RouteSummaryDto ToRouteSummary(DomainConfig config, IReadOnlyList<UpstreamTarget> upstreams) =>
        new(
            Domain: config.DomainName.Value,
            ForceHttps: config.ForceHttps,
            Source: "manual",
            TimeoutSeconds: (int)config.RequestTimeout.TotalSeconds,
            Upstreams: upstreams.Select(ToUpstreamInfo).ToList());

    public static UpstreamInfoDto ToUpstreamInfo(UpstreamTarget target) =>
        new(Url: target.Url.Value.ToString(), Weight: target.Weight);

    public static RouteDetailDto ToRouteDetail(
        DomainConfig config,
        IReadOnlyList<UpstreamTarget> upstreams,
        IReadOnlyList<UpstreamHealthStatus> health) =>
        new(
            Domain: config.DomainName.Value,
            ForceHttps: config.ForceHttps,
            TimeoutSeconds: (int)config.RequestTimeout.TotalSeconds,
            Source: "manual",
            Upstreams: upstreams.Select(ToUpstreamInfo).ToList(),
            Health: health.Select(h => new UpstreamHealthEntryDto(h.Url.Value.ToString(), h.IsHealthy)).ToList());

    public static CertificateSummaryDto ToCertificateSummary(DomainName domain, X509Certificate2 cert) =>
        new(
            Domain: domain.Value,
            Thumbprint: cert.Thumbprint,
            NotAfter: cert.NotAfter.ToString("O"),
            IsSelfSigned: cert.Issuer == cert.Subject);

    public static CertificateDetailDto ToCertificateDetail(DomainName domain, X509Certificate2 cert) =>
        new(
            Domain: domain.Value,
            Thumbprint: cert.Thumbprint,
            NotBefore: cert.NotBefore.ToString("O"),
            NotAfter: cert.NotAfter.ToString("O"),
            Issuer: cert.Issuer,
            IsSelfSigned: cert.Issuer == cert.Subject);

    public static bool CanMapToProxyEvent(IClusterEvent evt)
        => evt is DomainConfigured or DomainDeactivated or UpstreamHealthChanged or CertificateProvisioningRequested;

    public static ProxyEventDto ToProxyEvent(IClusterEvent evt) => evt switch
    {
        DomainConfigured e => new ProxyEventDto("RouteUpdated", e.Config.DomainName.Value, "", true, ""),
        DomainDeactivated e => new ProxyEventDto("RouteRemoved", e.DomainName.Value, "", false, ""),
        UpstreamHealthChanged e => new ProxyEventDto("UpstreamHealthChanged", "", "", e.IsHealthy, e.Url.Value.ToString()),
        CertificateProvisioningRequested e => new ProxyEventDto("CertificateProvisioned", e.DomainName.Value, "", true, ""),
        _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.GetType().Name, "Unmappable event")
    };
}
```

- [ ] **Step 5: Update Schleusenwerk.csproj — remove gRPC, add controller support**

Replace the content of `src/Schleusenwerk/Schleusenwerk.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

    <ItemGroup>
        <PackageReference Include="Akka.Streams" />
        <PackageReference Include="Akka.Cluster.Hosting" />
        <PackageReference Include="Akka.Persistence.Sql.Hosting" />
        <PackageReference Include="Docker.DotNet" />
        <PackageReference Include="Microsoft.Data.Sqlite" />
        <PackageReference Include="TurboHTTP" />
        <PackageReference Include="Servus.Akka" />
        <PackageReference Include="Servus.Core" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Schleusenwerk.Core\Schleusenwerk.Core.csproj" />
    </ItemGroup>

    <ItemGroup>
        <InternalsVisibleTo Include="Schleusenwerk.Tests" />
    </ItemGroup>

</Project>
```

Note: `Grpc.AspNetCore` is removed. The `Schleusenwerk.Contracts` project reference is also removed since we no longer need generated protobuf types — DTOs are now in `Api/ApiModels.cs`.

- [ ] **Step 6: Build to verify compilation**

Run: `dotnet build src/Schleusenwerk/Schleusenwerk.csproj`
Expected: Build will have errors because gRPC service references still exist in startup. That's expected — we fix them in Task 4.

- [ ] **Step 7: Commit**

```bash
git add src/Schleusenwerk/Api/ src/Schleusenwerk/Controllers/Generated/ src/Schleusenwerk/Schleusenwerk.csproj
git commit -m "feat(api): add REST API models, domain mapper, and NSwag-generated base controllers"
```

---

## Task 4: Implement REST Controllers (migrate from gRPC)

**Files:**
- Create: `src/Schleusenwerk/Controllers/RouteController.cs`
- Create: `src/Schleusenwerk/Controllers/CertificateController.cs`
- Create: `src/Schleusenwerk/Controllers/HealthController.cs`

- [ ] **Step 1: Create RouteController**

Create `src/Schleusenwerk/Controllers/RouteController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/routes")]
internal sealed class RouteController : ControllerBase
{
    private readonly IConfigurationService _config;

    public RouteController(IConfigurationService config) => _config = config;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RouteSummaryDto>>> ListRoutes(CancellationToken ct)
    {
        var result = await _config.GetAllAsync(ct);
        if (result is not ConfigurationResult<ConfigurationSnapshot>.Success success)
        {
            return Ok(Array.Empty<RouteSummaryDto>());
        }

        var routes = success.Value.Domains.Select(domain =>
        {
            var upstreams = success.Value.Upstreams.GetValueOrDefault(domain.DomainName.Value, []);
            return DomainModelMapper.ToRouteSummary(domain, upstreams);
        }).ToList();

        return Ok(routes);
    }

    [HttpGet("{domain}")]
    public async Task<ActionResult<RouteDetailDto>> GetRoute(string domain, CancellationToken ct)
    {
        var domainName = DomainName.Parse(domain);
        var result = await _config.GetByDomainAsync(domainName, ct);

        if (result is not ConfigurationResult<DomainConfigResult>.Success success)
        {
            return NotFound();
        }

        return Ok(DomainModelMapper.ToRouteDetail(success.Value.Config, success.Value.Upstreams, []));
    }

    [HttpPost]
    public async Task<ActionResult<CommandResultDto>> AddRoute([FromBody] AddRouteRequestDto request, CancellationToken ct)
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse(request.Domain),
            ForceHttps = request.ForceHttps,
            RequestTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30)
        };

        var addResult = await _config.AddDomainAsync(config, ct);
        if (!addResult.IsSuccess)
        {
            return Ok(CommandResultDto.Fail(((ConfigurationResult.Failure)addResult).Error));
        }

        if (!string.IsNullOrWhiteSpace(request.FirstUpstreamUrl))
        {
            var upstream = UpstreamTarget.Create(request.FirstUpstreamUrl);
            var upstreamResult = await _config.AddUpstreamAsync(config.DomainName, upstream, ct);
            if (!upstreamResult.IsSuccess)
            {
                return Ok(CommandResultDto.Fail(((ConfigurationResult.Failure)upstreamResult).Error));
            }
        }

        return Ok(CommandResultDto.Ok());
    }

    [HttpPut("{domain}")]
    public async Task<ActionResult<CommandResultDto>> UpdateRoute(
        string domain, [FromBody] UpdateRouteRequestDto request, CancellationToken ct)
    {
        var getResult = await _config.GetByDomainAsync(DomainName.Parse(domain), ct);
        if (getResult is not ConfigurationResult<DomainConfigResult>.Success existing)
        {
            return Ok(CommandResultDto.Fail("Domain not found"));
        }

        var updated = existing.Value.Config with
        {
            ForceHttps = request.ForceHttps,
            RequestTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30)
        };

        var result = await _config.UpdateDomainAsync(updated, ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }

    [HttpDelete("{domain}")]
    public async Task<ActionResult<CommandResultDto>> DeleteRoute(string domain, CancellationToken ct)
    {
        var result = await _config.RemoveDomainAsync(DomainName.Parse(domain), ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }

    [HttpPost("{domain}/upstreams")]
    public async Task<ActionResult<CommandResultDto>> AddUpstream(
        string domain, [FromBody] AddUpstreamRequestDto request, CancellationToken ct)
    {
        var upstream = UpstreamTarget.Create(request.Url, request.Weight > 0 ? request.Weight : 1);
        var result = await _config.AddUpstreamAsync(DomainName.Parse(domain), upstream, ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }

    [HttpDelete("{domain}/upstreams/{encodedUrl}")]
    public async Task<ActionResult<CommandResultDto>> RemoveUpstream(
        string domain, string encodedUrl, CancellationToken ct)
    {
        var urlBytes = Convert.FromBase64String(encodedUrl.Replace('-', '+').Replace('_', '/'));
        var url = System.Text.Encoding.UTF8.GetString(urlBytes);

        var result = await _config.RemoveUpstreamAsync(
            DomainName.Parse(domain),
            UpstreamUrl.Parse(url),
            ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }
}
```

- [ ] **Step 2: Create CertificateController**

Create `src/Schleusenwerk/Controllers/CertificateController.cs`:

```csharp
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Certificates;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/certificates")]
internal sealed class CertificateController : ControllerBase
{
    private readonly ICertificateStore _store;
    private readonly IReadOnlyActorRegistry _registry;

    public CertificateController(ICertificateStore store, IReadOnlyActorRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<CertificateSummaryDto>> ListCertificates()
    {
        var certs = new List<CertificateSummaryDto>();
        foreach (var domain in _store.ListDomains())
        {
            var cert = _store.GetCertificate(domain);
            if (cert is not null)
            {
                certs.Add(DomainModelMapper.ToCertificateSummary(domain, cert));
            }
        }
        return Ok(certs);
    }

    [HttpGet("{domain}")]
    public ActionResult<CertificateDetailDto> GetCertificate(string domain)
    {
        var domainName = DomainName.Parse(domain);
        var cert = _store.GetCertificate(domainName);

        if (cert is null)
        {
            return NotFound();
        }

        return Ok(DomainModelMapper.ToCertificateDetail(domainName, cert));
    }

    [HttpPost("{domain}/provision")]
    public ActionResult<CommandResultDto> ProvisionCertificate(string domain)
    {
        var eventHub = _registry.Get<EventHub>();
        eventHub.Tell(new CertificateProvisioningRequested(DomainName.Parse(domain)));
        return Ok(CommandResultDto.Ok());
    }
}
```

- [ ] **Step 3: Create HealthController**

Create `src/Schleusenwerk/Controllers/HealthController.cs`:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/health")]
internal sealed class HealthController : ControllerBase
{
    private readonly IConfigurationStore _store;
    private readonly IActorRef _domainRegion;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public HealthController(IConfigurationStore store, IReadOnlyActorRegistry registry)
    {
        _store = store;
        _domainRegion = registry.Get<DomainEntityActor>();
    }

    [HttpGet]
    public async Task<ActionResult<ProxyHealthResponseDto>> GetHealth(CancellationToken ct)
    {
        var domains = await _store.GetAllDomainsAsync(ct);
        var healthTasks = domains.Select(d => GetDomainHealth(d.DomainName, ct));
        var results = await Task.WhenAll(healthTasks);

        var healthyCount = 0;
        var unhealthyCount = 0;
        foreach (var entries in results)
        {
            if (entries.Any(e => e.IsHealthy))
            {
                healthyCount++;
            }
            else
            {
                unhealthyCount++;
            }
        }

        return Ok(new ProxyHealthResponseDto(domains.Count, healthyCount, unhealthyCount));
    }

    [HttpGet("{domain}")]
    public async Task<ActionResult<UpstreamHealthResponseDto>> GetUpstreamHealth(string domain, CancellationToken ct)
    {
        var domainName = DomainName.Parse(domain);
        var entries = await GetDomainHealth(domainName, ct);
        var upstreams = entries
            .Select(e => new UpstreamHealthEntryDto(e.Url.Value.ToString(), e.IsHealthy))
            .ToList();
        return Ok(new UpstreamHealthResponseDto(domain, upstreams));
    }

    private async Task<IReadOnlyList<UpstreamHealthStatus>> GetDomainHealth(DomainName domain, CancellationToken ct)
    {
        var result = await _domainRegion.Ask<DomainUpstreamHealthResult>(
            new GetDomainUpstreamHealth { Domain = domain.Value }, _timeout, ct);
        return result.Entries;
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk/Controllers/RouteController.cs src/Schleusenwerk/Controllers/CertificateController.cs src/Schleusenwerk/Controllers/HealthController.cs
git commit -m "feat(api): implement REST controllers migrated from gRPC services"
```

---

## Task 5: SignalR Hub + Event Bridge (move from UI to Proxy)

**Files:**
- Create: `src/Schleusenwerk/Hubs/ProxyEventHub.cs`
- Create: `src/Schleusenwerk/Hubs/EventBridgeService.cs`

- [ ] **Step 1: Create SignalR hub in proxy project**

Create `src/Schleusenwerk/Hubs/ProxyEventHub.cs`:

```csharp
using Microsoft.AspNetCore.SignalR;

namespace Schleusenwerk.Hubs;

public sealed class ProxyEventHub : Hub;
```

- [ ] **Step 2: Create EventBridgeService — bridges Akka EventHub to SignalR**

This replaces the old `EventStreamBackgroundService` from the UI project. Instead of gRPC streaming, it subscribes directly to the Akka EventHub actor and pushes to SignalR.

Create `src/Schleusenwerk/Hubs/EventBridgeService.cs`:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.SignalR;
using Schleusenwerk.Api;
using Schleusenwerk.Persistence;

namespace Schleusenwerk.Hubs;

internal sealed class EventBridgeService : BackgroundService
{
    private readonly IReadOnlyActorRegistry _registry;
    private readonly IMaterializer _materializer;
    private readonly IHubContext<ProxyEventHub> _hub;
    private readonly ILogger<EventBridgeService> _logger;

    public EventBridgeService(
        IReadOnlyActorRegistry registry,
        IMaterializer materializer,
        IHubContext<ProxyEventHub> hub,
        ILogger<EventBridgeService> logger)
    {
        _registry = registry;
        _materializer = materializer;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StreamEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Event bridge disconnected, retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task StreamEventsAsync(CancellationToken ct)
    {
        var eventHub = _registry.Get<EventHub>();
        var subscribed = await eventHub.Ask<EventHub.Subscribed>(
            EventHub.Subscribe.Instance, TimeSpan.FromSeconds(5), ct);

        await subscribed.SourceRef.Source
            .Where(DomainModelMapper.CanMapToProxyEvent)
            .RunForeach(async evt =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var dto = DomainModelMapper.ToProxyEvent(evt);
                await _hub.Clients.All.SendAsync("OnProxyEvent", dto, ct);
            }, _materializer)
            .ConfigureAwait(false);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Hubs/
git commit -m "feat(api): add SignalR ProxyEventHub and Akka EventBridge service"
```

---

## Task 6: Update Startup — Wire Controllers + SignalR, Remove gRPC

**Files:**
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`

- [ ] **Step 1: Update SchleusenwerkServicesSetup — replace gRPC with controllers + SignalR**

In `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`, replace:

```csharp
        services.AddGrpc();
        services.AddSingleton<IMaterializer>(sp =>
            sp.GetRequiredService<ActorSystem>().Materializer());
```

With:

```csharp
        services.AddControllers();
        services.AddSignalR();
        services.AddHostedService<Hubs.EventBridgeService>();
        services.AddSingleton<IMaterializer>(sp =>
            sp.GetRequiredService<ActorSystem>().Materializer());
```

Also add the required using at the top if not present:

```csharp
using Schleusenwerk.Hubs;
```

- [ ] **Step 2: Update SchleusenwerkApplicationSetup — replace gRPC mappings with controller + hub mappings**

In `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`, replace:

```csharp
        app.MapGrpcService<Grpc.RouteServiceImpl>();
        app.MapGrpcService<Grpc.CertificateServiceImpl>();
        app.MapGrpcService<Grpc.HealthServiceImpl>();
        app.MapGrpcService<Grpc.EventServiceImpl>();
```

With:

```csharp
        app.MapControllers();
        app.MapHub<Hubs.ProxyEventHub>("/hubs/events");
```

- [ ] **Step 3: Delete the entire Grpc directory**

```bash
rm src/Schleusenwerk/Grpc/RouteServiceImpl.cs
rm src/Schleusenwerk/Grpc/CertificateServiceImpl.cs
rm src/Schleusenwerk/Grpc/HealthServiceImpl.cs
rm src/Schleusenwerk/Grpc/EventServiceImpl.cs
rm src/Schleusenwerk/Grpc/ProtoMapper.cs
rmdir src/Schleusenwerk/Grpc
```

- [ ] **Step 4: Build the proxy project**

Run: `dotnet build src/Schleusenwerk/Schleusenwerk.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 5: Run existing tests to verify no regressions**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass (tests don't test gRPC services directly — they test actors and domain logic).

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk/Startup/ src/Schleusenwerk/Schleusenwerk.csproj
git rm -r src/Schleusenwerk/Grpc/
git commit -m "feat(api): wire REST controllers + SignalR, remove gRPC services"
```

---

## Task 7: Remove Blazor UI Project + Update Solution

**Files:**
- Delete: `src/Schleusenwerk.UI/` (entire project)
- Modify: `src/Schleusenwerk.slnx`
- Modify: `src/Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj`
- Modify: `src/Schleusenwerk.AppHost/Program.cs`
- Modify: `src/Directory.Packages.props`

- [ ] **Step 1: Remove UI project from solution**

Replace the content of `src/Schleusenwerk.slnx`:

```xml
<Solution>
  <Project Path="Schleusenwerk/Schleusenwerk.csproj" />
  <Project Path="Schleusenwerk.Core/Schleusenwerk.Core.csproj" />
  <Project Path="Schleusenwerk.Contracts/Schleusenwerk.Contracts.csproj" />
  <Project Path="Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj" />
  <Project Path="Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj" />
  <Folder Name="/Solution Items/">
    <File Path="../.dockerignore" />
    <File Path="../.gitignore" />
    <File Path="../CLAUDE.md" />
    <File Path="../docker-compose.yml" />
    <File Path="../README.md" />
    <File Path="Directory.Build.props" />
    <File Path="Directory.Packages.props" />
    <File Path="global.json" />
  </Folder>
  <Project Path="Schleusenwerk.Tests/Schleusenwerk.Tests.csproj" />
</Solution>
```

- [ ] **Step 2: Update AppHost — remove UI reference**

Replace the content of `src/Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Aspire.Hosting.AppHost" />
        <PackageReference Include="Aspire.Hosting.Orchestration.win-x64" />
        <PackageReference Include="Aspire.Dashboard.Sdk.win-x64" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\Schleusenwerk\Schleusenwerk.csproj" IsAspireProjectResource="true" />
    </ItemGroup>

</Project>
```

- [ ] **Step 3: Update AppHost Program.cs — remove UI, update proxy endpoints**

Replace the content of `src/Schleusenwerk.AppHost/Program.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var nginx = builder.AddContainer("upstream-mock", "nginx", "alpine")
    .WithBindMount("./nginx.conf", "/etc/nginx/nginx.conf", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 80, name: "http");

builder.AddProject<Projects.Schleusenwerk>("proxy")
    .WithHttpEndpoint(name: "http")
    .WithHttpsEndpoint(name: "https")
    .WithHttpEndpoint(port: 5000, name: "api")
    .WaitFor(nginx);

builder.Build().Run();
```

- [ ] **Step 4: Remove gRPC and MudBlazor package versions from Directory.Packages.props**

In `src/Directory.Packages.props`, remove the entire gRPC ItemGroup:

```xml
    <ItemGroup Label="gRPC">
        <PackageVersion Include="Google.Protobuf" Version="3.29.3"/>
        <PackageVersion Include="Grpc.Tools" Version="2.80.0"/>
        <PackageVersion Include="Grpc.Core.Api" Version="2.80.0"/>
        <PackageVersion Include="Grpc.AspNetCore" Version="2.80.0"/>
        <PackageVersion Include="Grpc.Net.Client" Version="2.80.0"/>
    </ItemGroup>
```

- [ ] **Step 5: Delete the entire UI project directory**

```bash
rm -rf src/Schleusenwerk.UI/
```

- [ ] **Step 6: Full solution build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 7: Run all tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git rm -r src/Schleusenwerk.UI/
git add src/Schleusenwerk.slnx src/Schleusenwerk.AppHost/ src/Directory.Packages.props
git commit -m "chore: remove Blazor UI project and gRPC packages, update solution"
```

---

## Task 8: Scaffold Vue.js Project

**Files:**
- Create: `src/Schleusenwerk.Web/package.json`
- Create: `src/Schleusenwerk.Web/tsconfig.json`
- Create: `src/Schleusenwerk.Web/tsconfig.app.json`
- Create: `src/Schleusenwerk.Web/tsconfig.node.json`
- Create: `src/Schleusenwerk.Web/vite.config.ts`
- Create: `src/Schleusenwerk.Web/index.html`
- Create: `src/Schleusenwerk.Web/src/main.ts`
- Create: `src/Schleusenwerk.Web/src/App.vue`
- Create: `src/Schleusenwerk.Web/src/env.d.ts`

- [ ] **Step 1: Create project with npm init + install dependencies**

```bash
mkdir -p src/Schleusenwerk.Web/src
cd src/Schleusenwerk.Web
npm init -y
npm install vue vue-router@4 pinia primevue @primevue/themes @microsoft/signalr
npm install -D vite @vitejs/plugin-vue typescript vue-tsc
```

- [ ] **Step 2: Create package.json scripts**

Update `src/Schleusenwerk.Web/package.json` scripts section:

```json
{
  "name": "schleusenwerk-web",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "vue-tsc -b && vite build",
    "preview": "vite preview",
    "generate-api": "nswag run ../Schleusenwerk.Contracts/nswag.json /variables:Output=src/api/generated/client.ts"
  }
}
```

(Keep the dependencies and devDependencies from npm install.)

- [ ] **Step 3: Create tsconfig files**

Create `src/Schleusenwerk.Web/tsconfig.json`:

```json
{
  "files": [],
  "references": [
    { "path": "./tsconfig.app.json" },
    { "path": "./tsconfig.node.json" }
  ]
}
```

Create `src/Schleusenwerk.Web/tsconfig.app.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "jsx": "preserve",
    "importHelpers": true,
    "skipLibCheck": true,
    "esModuleInterop": true,
    "allowSyntheticDefaultImports": true,
    "forceConsistentCasingInFileNames": true,
    "useDefineForClassFields": true,
    "sourceMap": true,
    "baseUrl": ".",
    "paths": {
      "@/*": ["./src/*"]
    },
    "lib": ["ES2022", "DOM", "DOM.Iterable"]
  },
  "include": ["src/**/*.ts", "src/**/*.tsx", "src/**/*.vue"],
  "exclude": ["node_modules"]
}
```

Create `src/Schleusenwerk.Web/tsconfig.node.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "skipLibCheck": true,
    "noEmit": true
  },
  "include": ["vite.config.ts"]
}
```

- [ ] **Step 4: Create vite.config.ts with API proxy**

Create `src/Schleusenwerk.Web/vite.config.ts`:

```typescript
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
```

- [ ] **Step 5: Create index.html**

Create `src/Schleusenwerk.Web/index.html`:

```html
<!DOCTYPE html>
<html lang="de">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Schleusenwerk</title>
  <link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;700&family=Inter:wght@400;500;600&display=swap" rel="stylesheet" />
</head>
<body>
  <div id="app"></div>
  <script type="module" src="/src/main.ts"></script>
</body>
</html>
```

- [ ] **Step 6: Create Vue type declaration**

Create `src/Schleusenwerk.Web/src/env.d.ts`:

```typescript
/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
```

- [ ] **Step 7: Create main.ts bootstrap**

Create `src/Schleusenwerk.Web/src/main.ts`:

```typescript
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import App from './App.vue'
import { router } from './router'
import './assets/blueprint.css'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(PrimeVue, { unstyled: true })

app.mount('#app')
```

- [ ] **Step 8: Create minimal App.vue shell**

Create `src/Schleusenwerk.Web/src/App.vue`:

```vue
<template>
  <div class="blueprint-grid"></div>
  <div style="position: relative; z-index: 1; min-height: 100vh;">
    <SchaltleisteNav />
    <main class="blueprint-content">
      <RouterView />
    </main>
  </div>
</template>

<script setup lang="ts">
import SchaltleisteNav from '@/components/SchaltleisteNav.vue'
</script>
```

- [ ] **Step 9: Verify the project builds**

Run from `src/Schleusenwerk.Web/`:
```bash
npx vue-tsc --noEmit
npx vite build
```

Expected: TypeScript check and build succeed (will have warnings about missing components/router — that's fine, we add them next).

- [ ] **Step 10: Commit**

```bash
git add src/Schleusenwerk.Web/
git commit -m "feat(web): scaffold Vue 3 + Vite + TypeScript + PrimeVue project"
```

---

## Task 9: Blueprint CSS + Router + Navigation

**Files:**
- Create: `src/Schleusenwerk.Web/src/assets/blueprint.css`
- Create: `src/Schleusenwerk.Web/src/router/index.ts`
- Create: `src/Schleusenwerk.Web/src/components/SchaltleisteNav.vue`

- [ ] **Step 1: Create blueprint.css**

Copy the Blueprint CSS from the existing spec (this is the same CSS from the Blazor plan, minus MudBlazor-specific overrides):

Create `src/Schleusenwerk.Web/src/assets/blueprint.css`:

```css
:root {
    --bp-background: #0a192f;
    --bp-surface: #112240;
    --bp-border: #1e3a5f;
    --bp-primary: #64ffda;
    --bp-text-primary: #ccd6f6;
    --bp-text-secondary: #8892b0;
    --bp-error: #ff6b6b;
    --bp-warning: #f0c000;
    --bp-info: #58a6ff;
    --bp-font-mono: 'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace;
    --bp-font-sans: 'Inter', 'Segoe UI', system-ui, sans-serif;
}

*, *::before, *::after {
    box-sizing: border-box;
}

html, body {
    background: var(--bp-background);
    color: var(--bp-text-primary);
    font-family: var(--bp-font-sans);
    margin: 0;
    line-height: 1.5;
}

a { color: inherit; text-decoration: none; }

.blueprint-grid {
    position: fixed;
    inset: 0;
    background-image:
        linear-gradient(rgba(100,200,255,0.03) 1px, transparent 1px),
        linear-gradient(90deg, rgba(100,200,255,0.03) 1px, transparent 1px);
    background-size: 24px 24px;
    pointer-events: none;
    z-index: 0;
}

.schaltleiste {
    background: var(--bp-surface);
    border-bottom: 1px solid var(--bp-border);
    height: 52px;
    display: flex;
    align-items: center;
    padding: 0 24px;
    position: relative;
    z-index: 10;
}

.schaltleiste-logo {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-right: 32px;
}

.schaltleiste-logo span {
    color: var(--bp-text-primary);
    font-family: var(--bp-font-mono);
    font-size: 13px;
    font-weight: 700;
    letter-spacing: 2px;
}

.schaltleiste-tabs {
    display: flex;
    height: 100%;
    gap: 0;
}

.schaltleiste-tab {
    display: flex;
    align-items: center;
    padding: 0 16px;
    border-bottom: 2px solid transparent;
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 12px;
    letter-spacing: 0.5px;
    text-decoration: none;
    transition: color 0.15s, border-color 0.15s;
    cursor: pointer;
    white-space: nowrap;
}

.schaltleiste-tab:hover { color: var(--bp-text-primary); }

.schaltleiste-tab.router-link-active,
.schaltleiste-tab.router-link-exact-active {
    color: var(--bp-primary);
    border-bottom-color: var(--bp-primary);
}

.schaltleiste-status {
    margin-left: auto;
    display: flex;
    align-items: center;
    gap: 8px;
}

.schaltleiste-status .dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--bp-primary);
    box-shadow: 0 0 6px rgba(100,255,218,0.4);
}

.schaltleiste-status .dot.error {
    background: var(--bp-error);
    box-shadow: 0 0 6px rgba(255,107,107,0.4);
}

.schaltleiste-status span {
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 11px;
}

.blueprint-content {
    position: relative;
    z-index: 1;
    max-width: 1200px;
    margin: 0 auto;
    padding: 24px;
}

.page-header {
    display: flex;
    align-items: baseline;
    gap: 12px;
    margin-bottom: 20px;
}

.page-header h1 {
    color: var(--bp-text-primary);
    font-family: var(--bp-font-mono);
    font-size: 18px;
    font-weight: 700;
    letter-spacing: 1px;
    margin: 0;
    text-transform: uppercase;
}

.page-header .separator {
    color: var(--bp-border);
    font-family: var(--bp-font-mono);
    font-size: 12px;
}

.page-header .subtitle {
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 12px;
}

.bp-panel {
    background: var(--bp-surface);
    border: 1px solid var(--bp-border);
    border-radius: 6px;
    padding: 16px;
}

.bp-panel-label {
    color: var(--bp-text-secondary);
    font-size: 9px;
    text-transform: uppercase;
    letter-spacing: 1.5px;
    margin-bottom: 12px;
}

.bp-kpi {
    background: var(--bp-surface);
    border: 1px solid var(--bp-border);
    border-radius: 6px;
    padding: 16px;
}

.bp-kpi-label {
    color: var(--bp-text-secondary);
    font-size: 9px;
    text-transform: uppercase;
    letter-spacing: 1.5px;
    margin-bottom: 6px;
}

.bp-kpi-value {
    font-family: var(--bp-font-mono);
    font-size: 28px;
    font-weight: 700;
}

.bp-badge {
    font-family: var(--bp-font-mono);
    font-size: 11px;
    padding: 2px 8px;
    border-radius: 3px;
    display: inline-block;
}

.bp-badge-primary { color: var(--bp-primary); background: rgba(100,255,218,0.08); }
.bp-badge-neutral { color: var(--bp-text-secondary); background: rgba(136,146,176,0.08); }
.bp-badge-warning { color: var(--bp-warning); background: rgba(240,192,0,0.08); }
.bp-badge-error { color: var(--bp-error); background: rgba(255,107,107,0.08); }

.bp-status-dot {
    width: 8px;
    height: 8px;
    border-radius: 2px;
    display: inline-block;
    flex-shrink: 0;
}

.bp-status-dot.offen { background: var(--bp-primary); }
.bp-status-dot.gesperrt { background: var(--bp-error); }
.bp-status-dot.warnung { background: var(--bp-warning); }
.bp-status-dot.neutral { background: var(--bp-text-secondary); }

.bp-btn-outline {
    background: transparent;
    border: 1px solid var(--bp-primary);
    color: var(--bp-primary);
    padding: 6px 14px;
    border-radius: 4px;
    font-family: var(--bp-font-mono);
    font-size: 12px;
    cursor: pointer;
    letter-spacing: 0.5px;
    transition: background 0.15s;
}

.bp-btn-outline:hover { background: rgba(100,255,218,0.08); }

.bp-btn-filled {
    background: var(--bp-primary);
    border: none;
    color: var(--bp-background);
    padding: 8px 16px;
    border-radius: 4px;
    font-family: var(--bp-font-mono);
    font-size: 12px;
    font-weight: 700;
    cursor: pointer;
    letter-spacing: 0.5px;
    transition: opacity 0.15s;
}

.bp-btn-filled:hover { opacity: 0.9; }
.bp-btn-filled:disabled { opacity: 0.5; cursor: not-allowed; }

.bp-live-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--bp-primary);
    box-shadow: 0 0 6px rgba(100,255,218,0.5);
    animation: bp-pulse 2s infinite;
}

@keyframes bp-pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.4; }
}

.bp-breadcrumb {
    color: var(--bp-text-secondary);
    font-family: var(--bp-font-mono);
    font-size: 11px;
    margin-bottom: 16px;
}

.bp-breadcrumb a {
    color: var(--bp-text-secondary);
    text-decoration: underline;
    text-decoration-color: var(--bp-border);
    text-underline-offset: 3px;
}

.bp-breadcrumb a:hover { color: var(--bp-primary); }
.bp-breadcrumb .current { color: var(--bp-primary); }

.bp-input {
    background: var(--bp-background);
    border: 1px solid var(--bp-border);
    border-radius: 4px;
    color: var(--bp-text-primary);
    font-family: var(--bp-font-mono);
    font-size: 13px;
    padding: 8px 12px;
    width: 100%;
    outline: none;
    transition: border-color 0.15s;
}

.bp-input:focus { border-color: var(--bp-primary); }
.bp-input::placeholder { color: var(--bp-text-secondary); opacity: 0.6; }

.bp-table {
    width: 100%;
    border-collapse: collapse;
    background: var(--bp-surface);
    border: 1px solid var(--bp-border);
    border-radius: 6px;
    overflow: hidden;
}

.bp-table th {
    color: var(--bp-text-secondary);
    font-size: 9px;
    text-transform: uppercase;
    letter-spacing: 1.5px;
    background: rgba(30,58,95,0.3);
    border-bottom: 1px solid var(--bp-border);
    padding: 10px 14px;
    text-align: left;
    font-weight: 400;
}

.bp-table td {
    color: var(--bp-text-primary);
    font-family: var(--bp-font-mono);
    font-size: 13px;
    border-bottom: 1px solid rgba(30,58,95,0.5);
    padding: 10px 14px;
}

.bp-table tr:hover td { background: rgba(100,255,218,0.02); }
.bp-table tr.error td { background: rgba(255,107,107,0.03); }

::-webkit-scrollbar { width: 8px; height: 8px; }
::-webkit-scrollbar-track { background: var(--bp-background); }
::-webkit-scrollbar-thumb { background: var(--bp-border); border-radius: 4px; }
::-webkit-scrollbar-thumb:hover { background: var(--bp-text-secondary); }
```

- [ ] **Step 2: Create router**

Create `src/Schleusenwerk.Web/src/router/index.ts`:

```typescript
import { createRouter, createWebHistory } from 'vue-router'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('@/pages/Leitstand.vue') },
    { path: '/tore', component: () => import('@/pages/Schleusentore.vue') },
    { path: '/tore/neu', component: () => import('@/pages/TorEinsetzen.vue') },
    { path: '/tore/:domain', component: () => import('@/pages/TorDetail.vue'), props: true },
    { path: '/siegel', component: () => import('@/pages/Siegel.vue') },
    { path: '/flussprotokoll', component: () => import('@/pages/Flussprotokoll.vue') },
    { path: '/hafenbecken', component: () => import('@/pages/Hafenbecken.vue') },
    { path: '/stellwerk', component: () => import('@/pages/Stellwerk.vue') },
  ],
})
```

- [ ] **Step 3: Create SchaltleisteNav component**

Create `src/Schleusenwerk.Web/src/components/SchaltleisteNav.vue`:

```vue
<template>
  <nav class="schaltleiste">
    <div class="schaltleiste-logo">
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none">
        <rect x="2" y="6" width="20" height="12" rx="2" stroke="#64ffda" stroke-width="1.5" fill="none" />
        <line x1="12" y1="6" x2="12" y2="18" stroke="#64ffda" stroke-width="1.5" stroke-dasharray="3 2" />
      </svg>
      <span>SCHLEUSENWERK</span>
    </div>

    <div class="schaltleiste-tabs">
      <RouterLink to="/" class="schaltleiste-tab" exact>LEITSTAND</RouterLink>
      <RouterLink to="/tore" class="schaltleiste-tab">SCHLEUSENTORE</RouterLink>
      <RouterLink to="/siegel" class="schaltleiste-tab">SIEGEL</RouterLink>
      <RouterLink to="/flussprotokoll" class="schaltleiste-tab">FLUSSPROTOKOLL</RouterLink>
      <RouterLink to="/hafenbecken" class="schaltleiste-tab">HAFENBECKEN</RouterLink>
      <RouterLink to="/stellwerk" class="schaltleiste-tab">STELLWERK</RouterLink>
    </div>

    <div class="schaltleiste-status">
      <div class="dot" :class="{ error: !systemOk }"></div>
      <span>{{ systemOk ? 'SYSTEM OK' : 'STÖRUNG' }}</span>
    </div>
  </nav>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useHealthStore } from '@/stores/health'

const health = useHealthStore()
const systemOk = computed(() => health.data?.unhealthyCount === 0)

onMounted(() => health.fetch())
</script>
```

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.Web/src/assets/blueprint.css src/Schleusenwerk.Web/src/router/ src/Schleusenwerk.Web/src/components/SchaltleisteNav.vue
git commit -m "feat(web): add Blueprint CSS, Vue Router, and Schaltleiste navigation"
```

---

## Task 10: API Client + Pinia Stores

**Files:**
- Create: `src/Schleusenwerk.Web/src/api/client.ts`
- Create: `src/Schleusenwerk.Web/src/api/signalr.ts`
- Create: `src/Schleusenwerk.Web/src/stores/health.ts`
- Create: `src/Schleusenwerk.Web/src/stores/routes.ts`
- Create: `src/Schleusenwerk.Web/src/stores/certificates.ts`
- Create: `src/Schleusenwerk.Web/src/stores/events.ts`

- [ ] **Step 1: Create typed API client**

Rather than depending on NSwag-generated TypeScript immediately (it requires the NSwag CLI on every dev machine), we write a thin hand-typed client that mirrors the OpenAPI spec. This can later be replaced by generated code.

Create `src/Schleusenwerk.Web/src/api/client.ts`:

```typescript
const BASE = '/api'

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (!res.ok && res.status === 404) {
    throw new Error('Not found')
  }
  return res.json()
}

export interface CommandResult {
  success: boolean
  errorMessage?: string | null
}

export interface RouteSummary {
  domain: string
  forceHttps: boolean
  source: string
  timeoutSeconds: number
  upstreams: UpstreamInfo[]
}

export interface UpstreamInfo {
  url: string
  weight: number
}

export interface RouteDetail {
  domain: string
  forceHttps: boolean
  timeoutSeconds: number
  source: string
  upstreams: UpstreamInfo[]
  health: UpstreamHealthEntry[]
}

export interface UpstreamHealthEntry {
  url: string
  isHealthy: boolean
}

export interface CertificateSummary {
  domain: string
  thumbprint: string
  notAfter: string
  isSelfSigned: boolean
}

export interface CertificateDetail {
  domain: string
  thumbprint: string
  notBefore: string
  notAfter: string
  issuer: string
  isSelfSigned: boolean
}

export interface ProxyHealthResponse {
  routeCount: number
  healthyCount: number
  unhealthyCount: number
}

export interface UpstreamHealthResponse {
  domain: string
  upstreams: UpstreamHealthEntry[]
}

export interface ProxyEventDto {
  type: string
  domain: string
  message: string
  isHealthy: boolean
  upstreamUrl: string
}

function toBase64Url(input: string): string {
  return btoa(input).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

export const api = {
  routes: {
    list: () => request<RouteSummary[]>('/routes'),
    get: (domain: string) => request<RouteDetail>(`/routes/${encodeURIComponent(domain)}`),
    add: (body: { domain: string; forceHttps?: boolean; timeoutSeconds?: number; firstUpstreamUrl?: string }) =>
      request<CommandResult>('/routes', { method: 'POST', body: JSON.stringify(body) }),
    update: (domain: string, body: { forceHttps: boolean; timeoutSeconds: number }) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}`, { method: 'PUT', body: JSON.stringify(body) }),
    delete: (domain: string) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}`, { method: 'DELETE' }),
    addUpstream: (domain: string, body: { url: string; weight?: number }) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}/upstreams`, { method: 'POST', body: JSON.stringify(body) }),
    removeUpstream: (domain: string, url: string) =>
      request<CommandResult>(`/routes/${encodeURIComponent(domain)}/upstreams/${toBase64Url(url)}`, { method: 'DELETE' }),
  },
  certificates: {
    list: () => request<CertificateSummary[]>('/certificates'),
    get: (domain: string) => request<CertificateDetail>(`/certificates/${encodeURIComponent(domain)}`),
    provision: (domain: string) =>
      request<CommandResult>(`/certificates/${encodeURIComponent(domain)}/provision`, { method: 'POST' }),
  },
  health: {
    get: () => request<ProxyHealthResponse>('/health'),
    getDomain: (domain: string) => request<UpstreamHealthResponse>(`/health/${encodeURIComponent(domain)}`),
  },
}
```

- [ ] **Step 2: Create SignalR connection manager**

Create `src/Schleusenwerk.Web/src/api/signalr.ts`:

```typescript
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import type { ProxyEventDto } from './client'

type EventHandler = (event: ProxyEventDto) => void

let connection: HubConnection | null = null
const handlers: EventHandler[] = []

export function onProxyEvent(handler: EventHandler) {
  handlers.push(handler)
  return () => {
    const idx = handlers.indexOf(handler)
    if (idx >= 0) { handlers.splice(idx, 1) }
  }
}

export async function startSignalR() {
  if (connection) { return }

  connection = new HubConnectionBuilder()
    .withUrl('/hubs/events')
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build()

  connection.on('OnProxyEvent', (evt: ProxyEventDto) => {
    handlers.forEach(h => h(evt))
  })

  await connection.start()
}

export function stopSignalR() {
  connection?.stop()
  connection = null
}
```

- [ ] **Step 3: Create health store**

Create `src/Schleusenwerk.Web/src/stores/health.ts`:

```typescript
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type ProxyHealthResponse } from '@/api/client'

export const useHealthStore = defineStore('health', () => {
  const data = ref<ProxyHealthResponse | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetch() {
    loading.value = true
    error.value = null
    try {
      data.value = await api.health.get()
    } catch (e: any) {
      error.value = e.message
    } finally {
      loading.value = false
    }
  }

  return { data, loading, error, fetch }
})
```

- [ ] **Step 4: Create routes store**

Create `src/Schleusenwerk.Web/src/stores/routes.ts`:

```typescript
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type RouteSummary, type RouteDetail, type CommandResult } from '@/api/client'

export const useRoutesStore = defineStore('routes', () => {
  const list = ref<RouteSummary[]>([])
  const detail = ref<RouteDetail | null>(null)
  const loading = ref(false)

  async function fetchList() {
    loading.value = true
    try {
      list.value = await api.routes.list()
    } finally {
      loading.value = false
    }
  }

  async function fetchDetail(domain: string) {
    loading.value = true
    try {
      detail.value = await api.routes.get(domain)
    } finally {
      loading.value = false
    }
  }

  async function addRoute(body: { domain: string; forceHttps?: boolean; timeoutSeconds?: number; firstUpstreamUrl?: string }): Promise<CommandResult> {
    return api.routes.add(body)
  }

  async function updateRoute(domain: string, body: { forceHttps: boolean; timeoutSeconds: number }): Promise<CommandResult> {
    return api.routes.update(domain, body)
  }

  async function deleteRoute(domain: string): Promise<CommandResult> {
    const result = await api.routes.delete(domain)
    if (result.success) {
      list.value = list.value.filter(r => r.domain !== domain)
    }
    return result
  }

  async function addUpstream(domain: string, body: { url: string; weight?: number }): Promise<CommandResult> {
    return api.routes.addUpstream(domain, body)
  }

  async function removeUpstream(domain: string, url: string): Promise<CommandResult> {
    return api.routes.removeUpstream(domain, url)
  }

  return { list, detail, loading, fetchList, fetchDetail, addRoute, updateRoute, deleteRoute, addUpstream, removeUpstream }
})
```

- [ ] **Step 5: Create certificates store**

Create `src/Schleusenwerk.Web/src/stores/certificates.ts`:

```typescript
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type CertificateSummary, type CommandResult } from '@/api/client'

export const useCertificatesStore = defineStore('certificates', () => {
  const list = ref<CertificateSummary[]>([])
  const loading = ref(false)

  async function fetchList() {
    loading.value = true
    try {
      list.value = await api.certificates.list()
    } finally {
      loading.value = false
    }
  }

  async function provision(domain: string): Promise<CommandResult> {
    return api.certificates.provision(domain)
  }

  return { list, loading, fetchList, provision }
})
```

- [ ] **Step 6: Create events store**

Create `src/Schleusenwerk.Web/src/stores/events.ts`:

```typescript
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { onProxyEvent, startSignalR } from '@/api/signalr'
import type { ProxyEventDto } from '@/api/client'

export interface FlowEntry {
  time: string
  type: string
  domain: string
  isHealthy: boolean
  upstream: string
  message: string
}

const MAX_EVENTS = 200

export const useEventsStore = defineStore('events', () => {
  const entries = ref<FlowEntry[]>([])
  const paused = ref(false)
  const connected = ref(false)
  const knownDomains = ref<Set<string>>(new Set())

  function init() {
    onProxyEvent((evt: ProxyEventDto) => {
      if (paused.value) { return }

      entries.value.unshift({
        time: new Date().toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
        type: evt.type,
        domain: evt.domain,
        isHealthy: evt.isHealthy,
        upstream: evt.upstreamUrl,
        message: evt.message,
      })

      if (evt.domain) {
        knownDomains.value.add(evt.domain)
      }

      if (entries.value.length > MAX_EVENTS) {
        entries.value.length = MAX_EVENTS
      }
    })

    startSignalR()
      .then(() => { connected.value = true })
      .catch(() => { connected.value = false })
  }

  function togglePause() { paused.value = !paused.value }

  return { entries, paused, connected, knownDomains, init, togglePause }
})
```

- [ ] **Step 7: Commit**

```bash
git add src/Schleusenwerk.Web/src/api/ src/Schleusenwerk.Web/src/stores/
git commit -m "feat(web): add API client, SignalR connection manager, and Pinia stores"
```

---

## Task 11: Shared Components

**Files:**
- Create: `src/Schleusenwerk.Web/src/components/StatusIndicator.vue`
- Create: `src/Schleusenwerk.Web/src/components/QuelleBadge.vue`
- Create: `src/Schleusenwerk.Web/src/components/SiegelIcon.vue`
- Create: `src/Schleusenwerk.Web/src/components/BpPanel.vue`
- Create: `src/Schleusenwerk.Web/src/components/BpKpi.vue`

- [ ] **Step 1: Create StatusIndicator**

Create `src/Schleusenwerk.Web/src/components/StatusIndicator.vue`:

```vue
<template>
  <span class="bp-status-dot" :class="status" :title="title"></span>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ status: 'offen' | 'gesperrt' | 'warnung' | 'neutral' }>()

const title = computed(() => {
  switch (props.status) {
    case 'offen': return 'Offen'
    case 'gesperrt': return 'Gesperrt'
    case 'warnung': return 'Warnung'
    default: return props.status
  }
})
</script>
```

- [ ] **Step 2: Create QuelleBadge**

Create `src/Schleusenwerk.Web/src/components/QuelleBadge.vue`:

```vue
<template>
  <span class="bp-badge" :class="source === 'docker' ? 'bp-badge-primary' : 'bp-badge-neutral'">
    {{ source === 'docker' ? 'docker' : 'manuell' }}
  </span>
</template>

<script setup lang="ts">
defineProps<{ source: string }>()
</script>
```

- [ ] **Step 3: Create SiegelIcon**

Create `src/Schleusenwerk.Web/src/components/SiegelIcon.vue`:

```vue
<template>
  <span :style="{ color, fontSize: '13px' }" :title="title">&#9670;</span>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ status: 'valid' | 'expiring' | 'self-signed' }>()

const color = computed(() => {
  switch (props.status) {
    case 'valid': return 'var(--bp-primary)'
    case 'expiring': return 'var(--bp-warning)'
    case 'self-signed': return 'var(--bp-text-secondary)'
    default: return 'var(--bp-text-secondary)'
  }
})

const title = computed(() => {
  switch (props.status) {
    case 'valid': return 'Gültiges Siegel'
    case 'expiring': return 'Siegel läuft bald ab'
    case 'self-signed': return 'Selbstsigniert'
    default: return 'Unbekannt'
  }
})
</script>
```

- [ ] **Step 4: Create BpPanel**

Create `src/Schleusenwerk.Web/src/components/BpPanel.vue`:

```vue
<template>
  <div class="bp-panel">
    <div v-if="label" class="bp-panel-label">{{ label }}</div>
    <slot />
  </div>
</template>

<script setup lang="ts">
defineProps<{ label?: string }>()
</script>
```

- [ ] **Step 5: Create BpKpi**

Create `src/Schleusenwerk.Web/src/components/BpKpi.vue`:

```vue
<template>
  <div class="bp-kpi">
    <div class="bp-kpi-label">{{ label }}</div>
    <div class="bp-kpi-value" :style="{ color }">{{ value }}</div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  label: string
  value: string | number
  color?: string
}>()
</script>
```

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk.Web/src/components/
git commit -m "feat(web): add shared Blueprint components (StatusIndicator, QuelleBadge, SiegelIcon, BpPanel, BpKpi)"
```

---

## Task 12: Pages — Leitstand + Schleusentore + TorEinsetzen + TorDetail

**Files:**
- Create: `src/Schleusenwerk.Web/src/pages/Leitstand.vue`
- Create: `src/Schleusenwerk.Web/src/pages/Schleusentore.vue`
- Create: `src/Schleusenwerk.Web/src/pages/TorEinsetzen.vue`
- Create: `src/Schleusenwerk.Web/src/pages/TorDetail.vue`

- [ ] **Step 1: Create Leitstand (Dashboard)**

Create `src/Schleusenwerk.Web/src/pages/Leitstand.vue`:

```vue
<template>
  <div>
    <div class="page-header">
      <h1>LEITSTAND</h1>
      <span class="separator">——</span>
      <span class="subtitle">Systemübersicht</span>
    </div>

    <div v-if="health.loading" style="color: var(--bp-text-secondary);">Laden...</div>
    <div v-else-if="health.error" style="color: var(--bp-error);">{{ health.error }}</div>
    <template v-else-if="health.data">
      <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px;">
        <BpKpi label="Tore gesamt" :value="health.data.routeCount" color="var(--bp-text-primary)" />
        <BpKpi label="Tore offen" :value="health.data.healthyCount" color="var(--bp-primary)" />
        <BpKpi label="Tore gesperrt" :value="health.data.unhealthyCount" color="var(--bp-error)" />
        <BpKpi label="Siegel ablaufend" :value="expiringCerts" color="var(--bp-warning)" />
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import BpKpi from '@/components/BpKpi.vue'
import { useHealthStore } from '@/stores/health'
import { useCertificatesStore } from '@/stores/certificates'

const health = useHealthStore()
const certs = useCertificatesStore()
const expiringCerts = ref(0)

onMounted(async () => {
  await Promise.all([health.fetch(), certs.fetchList()])
  const now = Date.now()
  const fourteenDays = 14 * 24 * 60 * 60 * 1000
  expiringCerts.value = certs.list.filter(c => {
    const expiry = new Date(c.notAfter).getTime()
    return expiry - now < fourteenDays
  }).length
})
</script>
```

- [ ] **Step 2: Create Schleusentore (Route List)**

Create `src/Schleusenwerk.Web/src/pages/Schleusentore.vue`:

```vue
<template>
  <div>
    <div class="page-header">
      <h1>SCHLEUSENTORE</h1>
      <span class="separator">——</span>
      <span class="subtitle">{{ routes.list.length }} Tore konfiguriert</span>
      <div style="margin-left: auto;">
        <RouterLink to="/tore/neu" class="bp-btn-outline">+ TOR EINSETZEN</RouterLink>
      </div>
    </div>

    <div v-if="routes.loading" style="color: var(--bp-text-secondary);">Laden...</div>
    <table v-else class="bp-table">
      <thead>
        <tr>
          <th style="width: 28px;"></th>
          <th>Domain</th>
          <th>Quelle</th>
          <th>Kammern</th>
          <th>Siegel</th>
          <th style="width: 60px;"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="route in routes.list" :key="route.domain">
          <td><StatusIndicator status="offen" /></td>
          <td>
            <RouterLink :to="`/tore/${route.domain}`" style="color: var(--bp-text-primary);">
              {{ route.domain }}
            </RouterLink>
          </td>
          <td><QuelleBadge :source="route.source" /></td>
          <td style="text-align: center;">{{ route.upstreams.length }}</td>
          <td style="text-align: center;">
            <SiegelIcon :status="getCertStatus(route.domain)" />
          </td>
          <td>
            <button style="background: none; border: none; color: var(--bp-error); cursor: pointer; font-size: 16px;"
                    @click="handleDelete(route.domain)">✕</button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import StatusIndicator from '@/components/StatusIndicator.vue'
import QuelleBadge from '@/components/QuelleBadge.vue'
import SiegelIcon from '@/components/SiegelIcon.vue'
import { useRoutesStore } from '@/stores/routes'
import { useCertificatesStore } from '@/stores/certificates'

const routes = useRoutesStore()
const certs = useCertificatesStore()
const certStatus = ref<Record<string, 'valid' | 'expiring' | 'self-signed'>>({})

onMounted(async () => {
  await Promise.all([routes.fetchList(), certs.fetchList()])
  const now = Date.now()
  const fourteenDays = 14 * 24 * 60 * 60 * 1000
  for (const cert of certs.list) {
    if (cert.isSelfSigned) {
      certStatus.value[cert.domain] = 'self-signed'
    } else if (new Date(cert.notAfter).getTime() - now < fourteenDays) {
      certStatus.value[cert.domain] = 'expiring'
    } else {
      certStatus.value[cert.domain] = 'valid'
    }
  }
})

function getCertStatus(domain: string): 'valid' | 'expiring' | 'self-signed' {
  return certStatus.value[domain] ?? 'valid'
}

async function handleDelete(domain: string) {
  await routes.deleteRoute(domain)
}
</script>
```

- [ ] **Step 3: Create TorEinsetzen (Route Create)**

Create `src/Schleusenwerk.Web/src/pages/TorEinsetzen.vue`:

```vue
<template>
  <div>
    <div class="bp-breadcrumb">
      <RouterLink to="/tore">SCHLEUSENTORE</RouterLink>
      <span style="color: var(--bp-border); margin: 0 6px;">/</span>
      <span class="current">NEU</span>
    </div>

    <div class="page-header">
      <h1>TOR EINSETZEN</h1>
    </div>

    <div class="bp-panel" style="max-width: 600px;">
      <div style="margin-bottom: 16px;">
        <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Domain</label>
        <input v-model="domain" class="bp-input" placeholder="example.com" />
      </div>
      <div style="margin-bottom: 16px;">
        <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Erste Kammer</label>
        <input v-model="firstUpstreamUrl" class="bp-input" placeholder="http://backend:8080" />
      </div>
      <div style="margin-bottom: 16px; display: flex; align-items: center; gap: 8px;">
        <input type="checkbox" v-model="forceHttps" />
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">Versiegelt (HTTPS)</span>
      </div>
      <div style="display: flex; gap: 12px; margin-top: 16px;">
        <button class="bp-btn-filled" @click="submit" :disabled="submitting">
          {{ submitting ? 'Wird eingesetzt...' : 'TOR EINSETZEN' }}
        </button>
        <RouterLink to="/tore" style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px; padding: 8px 16px;">
          Abbrechen
        </RouterLink>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useRoutesStore } from '@/stores/routes'

const router = useRouter()
const routes = useRoutesStore()

const domain = ref('')
const firstUpstreamUrl = ref('')
const forceHttps = ref(false)
const submitting = ref(false)

async function submit() {
  if (!domain.value) { return }
  submitting.value = true
  try {
    const result = await routes.addRoute({
      domain: domain.value,
      forceHttps: forceHttps.value,
      timeoutSeconds: 30,
      firstUpstreamUrl: firstUpstreamUrl.value || undefined,
    })
    if (result.success) {
      router.push('/tore')
    }
  } finally {
    submitting.value = false
  }
}
</script>
```

- [ ] **Step 4: Create TorDetail (Route Detail)**

Create `src/Schleusenwerk.Web/src/pages/TorDetail.vue`:

```vue
<template>
  <div>
    <div class="bp-breadcrumb">
      <RouterLink to="/tore">SCHLEUSENTORE</RouterLink>
      <span style="color: var(--bp-border); margin: 0 6px;">/</span>
      <span class="current">{{ domain }}</span>
    </div>

    <div v-if="routes.loading" style="color: var(--bp-text-secondary);">Laden...</div>
    <div v-else-if="!routes.detail" style="color: var(--bp-error);">Tor nicht gefunden.</div>
    <template v-else>
      <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 24px;">
        <StatusIndicator :status="isHealthy ? 'offen' : 'gesperrt'" />
        <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 18px; font-weight: 700;">{{ domain }}</span>
        <span class="bp-badge" :class="isHealthy ? 'bp-badge-primary' : 'bp-badge-error'">
          {{ isHealthy ? 'offen' : 'gesperrt' }}
        </span>
        <span v-if="routes.detail.forceHttps" class="bp-badge bp-badge-primary">versiegelt</span>
      </div>

      <div style="display: grid; grid-template-columns: 280px 1fr; gap: 24px;">
        <BpPanel label="Konfiguration">
          <div style="margin-bottom: 12px; display: flex; align-items: center; gap: 8px;">
            <input type="checkbox" v-model="forceHttps" />
            <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">Versiegelung (HTTPS)</span>
          </div>
          <div style="margin-bottom: 16px;">
            <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Timeout (s)</label>
            <input v-model.number="timeoutSeconds" type="number" min="5" max="300" class="bp-input" />
          </div>
          <button class="bp-btn-filled" style="width: 100%;" @click="saveConfig">SPEICHERN</button>
        </BpPanel>

        <BpPanel label="Kammern">
          <div style="display: flex; flex-direction: column; gap: 8px; margin-bottom: 14px;">
            <div v-for="upstream in routes.detail.upstreams" :key="upstream.url"
                 style="display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 1px solid rgba(30,58,95,0.8); border-radius: 4px;">
              <StatusIndicator :status="isUpstreamHealthy(upstream.url) ? 'offen' : 'gesperrt'" />
              <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 12px; flex: 1;">{{ upstream.url }}</span>
              <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 11px;">w:{{ upstream.weight }}</span>
              <button style="background: none; border: none; color: var(--bp-error); cursor: pointer;" @click="handleRemoveUpstream(upstream.url)">✕</button>
            </div>
          </div>
          <div style="display: flex; gap: 8px;">
            <input v-model="newUpstreamUrl" class="bp-input" placeholder="http://upstream:port" style="flex: 1;" />
            <button class="bp-btn-outline" @click="handleAddUpstream">+ KAMMER</button>
          </div>
        </BpPanel>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import StatusIndicator from '@/components/StatusIndicator.vue'
import BpPanel from '@/components/BpPanel.vue'
import { useRoutesStore } from '@/stores/routes'

const props = defineProps<{ domain: string }>()
const routes = useRoutesStore()

const forceHttps = ref(false)
const timeoutSeconds = ref(30)
const newUpstreamUrl = ref('')

const isHealthy = computed(() =>
  routes.detail?.health.every(h => h.isHealthy) ?? true)

function isUpstreamHealthy(url: string): boolean {
  return routes.detail?.health.find(h => h.url === url)?.isHealthy ?? true
}

onMounted(async () => {
  await routes.fetchDetail(props.domain)
  if (routes.detail) {
    forceHttps.value = routes.detail.forceHttps
    timeoutSeconds.value = routes.detail.timeoutSeconds || 30
  }
})

async function saveConfig() {
  await routes.updateRoute(props.domain, {
    forceHttps: forceHttps.value,
    timeoutSeconds: timeoutSeconds.value,
  })
}

async function handleAddUpstream() {
  if (!newUpstreamUrl.value) { return }
  const result = await routes.addUpstream(props.domain, { url: newUpstreamUrl.value })
  if (result.success) {
    newUpstreamUrl.value = ''
    await routes.fetchDetail(props.domain)
  }
}

async function handleRemoveUpstream(url: string) {
  const result = await routes.removeUpstream(props.domain, url)
  if (result.success) {
    await routes.fetchDetail(props.domain)
  }
}
</script>
```

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk.Web/src/pages/Leitstand.vue src/Schleusenwerk.Web/src/pages/Schleusentore.vue src/Schleusenwerk.Web/src/pages/TorEinsetzen.vue src/Schleusenwerk.Web/src/pages/TorDetail.vue
git commit -m "feat(web): add Leitstand, Schleusentore, TorEinsetzen, and TorDetail pages"
```

---

## Task 13: Pages — Siegel + Flussprotokoll + Hafenbecken + Stellwerk

**Files:**
- Create: `src/Schleusenwerk.Web/src/pages/Siegel.vue`
- Create: `src/Schleusenwerk.Web/src/pages/Flussprotokoll.vue`
- Create: `src/Schleusenwerk.Web/src/pages/Hafenbecken.vue`
- Create: `src/Schleusenwerk.Web/src/pages/Stellwerk.vue`

- [ ] **Step 1: Create Siegel (Certificates)**

Create `src/Schleusenwerk.Web/src/pages/Siegel.vue`:

```vue
<template>
  <div>
    <div class="page-header">
      <h1>SIEGEL</h1>
      <span class="separator">——</span>
      <span class="subtitle">TLS-Zertifikate</span>
    </div>

    <div v-if="certs.loading" style="color: var(--bp-text-secondary);">Laden...</div>
    <table v-else class="bp-table">
      <thead>
        <tr>
          <th style="width: 28px;"></th>
          <th>Domain</th>
          <th>Fingerabdruck</th>
          <th>Gültig bis</th>
          <th>Typ</th>
          <th style="width: 80px;"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="cert in certs.list" :key="cert.domain">
          <td><SiegelIcon :status="getSiegelStatus(cert)" /></td>
          <td style="color: var(--bp-text-primary);">{{ cert.domain }}</td>
          <td style="font-size: 11px; color: var(--bp-text-secondary);">{{ cert.thumbprint.slice(0, 12) }}…</td>
          <td>
            <span :style="{ color: isExpiring(cert) ? 'var(--bp-warning)' : 'var(--bp-primary)' }">
              {{ formatDate(cert.notAfter) }}
            </span>
          </td>
          <td>
            <span class="bp-badge" :class="cert.isSelfSigned ? 'bp-badge-warning' : 'bp-badge-primary'">
              {{ cert.isSelfSigned ? 'Selbst' : 'ACME' }}
            </span>
          </td>
          <td>
            <button class="bp-btn-outline" style="font-size: 11px; padding: 2px 8px;" @click="renew(cert.domain)">
              Erneuern
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import SiegelIcon from '@/components/SiegelIcon.vue'
import { useCertificatesStore } from '@/stores/certificates'
import type { CertificateSummary } from '@/api/client'

const certs = useCertificatesStore()

onMounted(() => certs.fetchList())

function isExpiring(cert: CertificateSummary): boolean {
  const expiry = new Date(cert.notAfter).getTime()
  return expiry - Date.now() < 14 * 24 * 60 * 60 * 1000
}

function getSiegelStatus(cert: CertificateSummary): 'valid' | 'expiring' | 'self-signed' {
  if (cert.isSelfSigned) { return 'self-signed' }
  if (isExpiring(cert)) { return 'expiring' }
  return 'valid'
}

function formatDate(iso: string): string {
  try { return new Date(iso).toISOString().slice(0, 10) } catch { return iso }
}

async function renew(domain: string) {
  await certs.provision(domain)
}
</script>
```

- [ ] **Step 2: Create Flussprotokoll (Live Events)**

Create `src/Schleusenwerk.Web/src/pages/Flussprotokoll.vue`:

```vue
<template>
  <div>
    <div class="page-header">
      <h1>FLUSSPROTOKOLL</h1>
      <span class="separator">——</span>
      <div class="bp-live-dot"></div>
      <span style="color: var(--bp-primary); font-family: var(--bp-font-mono); font-size: 11px;">LIVE</span>
      <div style="margin-left: auto;">
        <button class="bp-btn-outline" @click="events.togglePause">
          {{ events.paused ? '▶ Fortsetzen' : '⏸ Pausieren' }}
        </button>
      </div>
    </div>

    <div class="bp-panel" style="padding: 0; overflow: hidden;">
      <div style="display: grid; grid-template-columns: 80px 180px 1fr 50px 120px; padding: 10px 14px; border-bottom: 1px solid var(--bp-border); background: rgba(30,58,95,0.3);">
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">ZEIT</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">TYP</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">TOR</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">STATUS</span>
        <span style="color: var(--bp-text-secondary); font-size: 9px; letter-spacing: 1px;">INFO</span>
      </div>

      <div v-for="(evt, i) in events.entries" :key="i"
           style="display: grid; grid-template-columns: 80px 180px 1fr 50px 120px; padding: 7px 14px; border-bottom: 1px solid rgba(30,58,95,0.4); align-items: center; font-family: var(--bp-font-mono); font-size: 12px;">
        <span style="color: var(--bp-border);">{{ evt.time }}</span>
        <span :style="{ color: getTypeColor(evt.type) }">{{ evt.type }}</span>
        <span style="color: var(--bp-text-primary);">{{ evt.domain }}</span>
        <span :style="{ color: evt.isHealthy ? 'var(--bp-primary)' : 'var(--bp-error)' }">
          {{ evt.isHealthy ? '→' : '⊧' }}
        </span>
        <span style="color: var(--bp-text-secondary); font-size: 11px;">{{ evt.upstream || evt.message }}</span>
      </div>

      <div v-if="events.entries.length === 0"
           style="padding: 24px; text-align: center; color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
        Warte auf Ereignisse…
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useEventsStore } from '@/stores/events'

const events = useEventsStore()

onMounted(() => events.init())

function getTypeColor(type: string): string {
  switch (type) {
    case 'RouteUpdated': return 'var(--bp-primary)'
    case 'RouteRemoved': return 'var(--bp-error)'
    case 'UpstreamHealthChanged': return 'var(--bp-warning)'
    case 'CertificateProvisioned': return 'var(--bp-primary)'
    case 'CertificateExpiring': return 'var(--bp-warning)'
    default: return 'var(--bp-text-secondary)'
  }
}
</script>
```

- [ ] **Step 3: Create Hafenbecken (Docker Discovery) — placeholder**

Create `src/Schleusenwerk.Web/src/pages/Hafenbecken.vue`:

```vue
<template>
  <div>
    <div class="page-header">
      <h1>HAFENBECKEN</h1>
      <span class="separator">——</span>
      <span class="subtitle">Docker-Erkennung</span>
    </div>

    <div class="bp-panel" style="text-align: center;">
      <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
        Docker Discovery API noch nicht implementiert — wird über einen separaten Endpunkt nachgeliefert.
      </span>
    </div>
  </div>
</template>
```

- [ ] **Step 4: Create Stellwerk (Settings)**

Create `src/Schleusenwerk.Web/src/pages/Stellwerk.vue`:

```vue
<template>
  <div>
    <div class="page-header">
      <h1>STELLWERK</h1>
      <span class="separator">——</span>
      <span class="subtitle">Systemkonfiguration</span>
    </div>

    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 24px;">
      <BpPanel label="ACME-Konfiguration">
        <div style="margin-bottom: 12px;">
          <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Kontakt-E-Mail</label>
          <input class="bp-input" placeholder="admin@example.com" disabled />
        </div>
        <div style="margin-bottom: 16px;">
          <label style="color: var(--bp-text-secondary); font-size: 11px; display: block; margin-bottom: 4px;">Anbieter</label>
          <input class="bp-input" value="Let's Encrypt" disabled />
        </div>
        <button class="bp-btn-filled" disabled>SPEICHERN</button>
        <p style="color: var(--bp-text-secondary); font-size: 11px; margin-top: 8px;">
          Konfiguration wird über appsettings.json verwaltet
        </p>
      </BpPanel>

      <BpPanel label="Systeminformation">
        <div style="font-family: var(--bp-font-mono); font-size: 12px; line-height: 2.2;">
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">Version</span>
            <span>0.1.0</span>
          </div>
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">Tore</span>
            <span style="color: var(--bp-primary);">{{ health.data?.routeCount ?? '—' }}</span>
          </div>
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--bp-text-secondary);">Status</span>
            <span :style="{ color: isHealthy ? 'var(--bp-primary)' : 'var(--bp-error)' }">
              {{ isHealthy ? 'Alle Systeme offen' : 'Störungen vorhanden' }}
            </span>
          </div>
        </div>
      </BpPanel>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, computed } from 'vue'
import BpPanel from '@/components/BpPanel.vue'
import { useHealthStore } from '@/stores/health'

const health = useHealthStore()
const isHealthy = computed(() => health.data?.unhealthyCount === 0)

onMounted(() => health.fetch())
</script>
```

- [ ] **Step 5: Verify Vue build**

Run from `src/Schleusenwerk.Web/`:
```bash
npx vue-tsc --noEmit && npx vite build
```

Expected: TypeScript check passes, Vite build produces `dist/` directory.

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk.Web/src/pages/
git commit -m "feat(web): add Siegel, Flussprotokoll, Hafenbecken, and Stellwerk pages"
```

---

## Task 14: Docker + Deployment Config

**Files:**
- Create: `src/Schleusenwerk.Web/Dockerfile`
- Create: `src/Schleusenwerk.Web/nginx.conf`
- Modify: `docker-compose.yml`

- [ ] **Step 1: Create nginx config**

Create `src/Schleusenwerk.Web/nginx.conf`:

```nginx
server {
    listen 80;
    server_name _;
    root /usr/share/nginx/html;
    index index.html;

    location /api/ {
        proxy_pass http://proxy:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }

    location /hubs/ {
        proxy_pass http://proxy:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

- [ ] **Step 2: Create Dockerfile**

Create `src/Schleusenwerk.Web/Dockerfile`:

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

- [ ] **Step 3: Update docker-compose.yml**

Replace the content of `docker-compose.yml`:

```yaml
version: '3.8'

services:
  proxy:
    build:
      context: .
      dockerfile: src/Schleusenwerk/Dockerfile
    ports:
      - "1312:80"
      - "1313:443"
      - "5000:5000"
    volumes:
      - ${DOCKER_SOCKET:-/var/run/docker.sock}:/var/run/docker.sock
      - db:/data
      - certs:/certs
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    networks:
      - schleusenwerk

  web:
    build:
      context: src/Schleusenwerk.Web
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    networks:
      - schleusenwerk
    depends_on:
      - proxy

volumes:
  db:
  certs:

networks:
  schleusenwerk:
    driver: bridge
```

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.Web/Dockerfile src/Schleusenwerk.Web/nginx.conf docker-compose.yml
git commit -m "feat(deploy): add Vue.js Docker build with nginx reverse proxy, update docker-compose"
```

---

## Task 15: Update CLAUDE.md + Final Verification

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update CLAUDE.md architecture section**

In `CLAUDE.md`, update the Architecture section to reflect the new structure. Replace the `UI` line:

```
UI              (Schleusenwerk/UI/)        — Blazor Server under /manage, SignalR event bridge
```

With:

```
API             (Schleusenwerk/Controllers/) — REST API (OpenAPI-generated base controllers)
Hubs            (Schleusenwerk/Hubs/)      — SignalR ProxyEventHub + Akka EventBridge
Web             (Schleusenwerk.Web/)       — Vue 3 SPA, PrimeVue, Pinia, Blueprint theme
```

- [ ] **Step 2: Update Key Technology Decisions**

Add/replace the UI and HTTP Client bullets:

```
- **Management API:** REST/JSON with OpenAPI contract-first (NSwag generates C# base controllers + TypeScript client)
- **UI:** Vue 3 + Vite + TypeScript SPA with PrimeVue (Unstyled) and Blueprint theme, served by nginx
- **Live Events:** SignalR WebSocket hub bridged from Akka EventHub via EventBridgeService
```

Remove any references to gRPC, Blazor, or MudBlazor.

- [ ] **Step 3: Full solution build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Run all tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 5: Vue build verification**

Run from `src/Schleusenwerk.Web/`:
```bash
npm ci && npm run build
```

Expected: Build succeeds, `dist/` contains `index.html` + JS/CSS assets.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for Vue.js + OpenAPI architecture"
```
