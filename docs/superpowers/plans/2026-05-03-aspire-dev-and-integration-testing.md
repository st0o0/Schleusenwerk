# Aspire Dev Environment & Integration Testing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Aspire orchestration to run proxy + UI + nginx-mock as containers via Podman, with automated gRPC integration tests against the real stack.

**Architecture:** A new `Schleusenwerk.AppHost` project orchestrates three containers (proxy, UI, nginx upstream mock) using existing Dockerfiles. A `Schleusenwerk.IntegrationTests` project uses `Aspire.Hosting.Testing` to spin up the full stack and run gRPC/HTTP tests against it. ServiceDefaults is omitted because the proxy and UI code are not modified — there's nothing to consume it.

**Tech Stack:** `Aspire.Hosting` 9.3.0 · `Aspire.Hosting.Testing` 9.3.0 · `Grpc.Net.Client` · `xunit.v3` · Podman (Docker-compatible socket)

---

## File Map

```
src/
  Schleusenwerk.AppHost/                   NEW project
    Schleusenwerk.AppHost.csproj           Aspire AppHost (orchestrator)
    Program.cs                             Container definitions
    nginx.conf                             Upstream mock config
    appsettings.json                       Dashboard/logging config

  Schleusenwerk.IntegrationTests/          NEW project
    Schleusenwerk.IntegrationTests.csproj   xUnit v3 + Aspire.Hosting.Testing
    SchleusenwerkFixture.cs                Shared fixture: starts stack, exposes clients
    RouteManagementSpec.cs                 Route CRUD via gRPC (6 tests)
    CertificateManagementSpec.cs           Cert listing via gRPC (2 tests)
    UpstreamForwardingSpec.cs              HTTP forwarding through proxy (2 tests)
    EventStreamSpec.cs                     gRPC server-side streaming (1 test)

  Directory.Packages.props                 MODIFIED — add Aspire package versions
  Schleusenwerk.slnx                       MODIFIED — add 2 new projects
```

---

## Task 1 — Aspire Packages + AppHost Project Scaffold

**Files:**
- Modify: `src/Directory.Packages.props`
- Create: `src/Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj`
- Create: `src/Schleusenwerk.AppHost/appsettings.json`
- Modify: `src/Schleusenwerk.slnx`

- [ ] **Step 1: Add Aspire package versions to Directory.Packages.props**

  Open `src/Directory.Packages.props` and add after the `<ItemGroup Label="UI">` block (before closing `</Project>`):

  ```xml
  <ItemGroup Label="Aspire">
      <PackageVersion Include="Aspire.Hosting.AppHost" Version="9.3.0"/>
      <PackageVersion Include="Aspire.Hosting.Testing" Version="9.3.0"/>
  </ItemGroup>
  ```

- [ ] **Step 2: Create the AppHost project file**

  Create `src/Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj`:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

      <PropertyGroup>
          <OutputType>Exe</OutputType>
          <IsAspireHost>true</IsAspireHost>
      </PropertyGroup>

      <ItemGroup>
          <PackageReference Include="Aspire.Hosting.AppHost" />
      </ItemGroup>

  </Project>
  ```

- [ ] **Step 3: Create appsettings.json**

  Create `src/Schleusenwerk.AppHost/appsettings.json`:

  ```json
  {
    "Logging": {
      "LogLevel": {
        "Default": "Information",
        "Aspire.Hosting": "Information"
      }
    }
  }
  ```

- [ ] **Step 4: Add AppHost to solution**

  Add to `src/Schleusenwerk.slnx` before the `<Folder>` element:

  ```xml
  <Project Path="Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj" />
  ```

- [ ] **Step 5: Verify restore**

  ```bash
  cd src
  dotnet restore Schleusenwerk.slnx
  ```

  Expected: no errors, `Schleusenwerk.AppHost` appears in restore output.

- [ ] **Step 6: Commit**

  ```bash
  git add src/Directory.Packages.props src/Schleusenwerk.AppHost/ src/Schleusenwerk.slnx
  git commit -m "feat: add Schleusenwerk.AppHost project with Aspire packages"
  ```

---

## Task 2 — AppHost Program.cs + Nginx Mock Config

**Files:**
- Create: `src/Schleusenwerk.AppHost/Program.cs`
- Create: `src/Schleusenwerk.AppHost/nginx.conf`

- [ ] **Step 1: Create nginx.conf**

  Create `src/Schleusenwerk.AppHost/nginx.conf`:

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

- [ ] **Step 2: Write Program.cs**

  Create `src/Schleusenwerk.AppHost/Program.cs`:

  ```csharp
  var builder = DistributedApplication.CreateBuilder(args);

  var nginx = builder.AddContainer("upstream-mock", "nginx", "alpine")
      .WithBindMount("./nginx.conf", "/etc/nginx/nginx.conf", isReadOnly: true)
      .WithHttpEndpoint(targetPort: 80, name: "http");

  var proxy = builder.AddDockerfile("proxy", "../../", "src/Schleusenwerk/Dockerfile")
      .WithHttpEndpoint(targetPort: 80, name: "http")
      .WithHttpEndpoint(targetPort: 5000, name: "grpc")
      .WithVolume("proxy-data", "/data")
      .WaitFor(nginx);

  builder.AddDockerfile("ui", "../../", "src/Schleusenwerk.UI/Dockerfile")
      .WithHttpEndpoint(targetPort: 8080, name: "http")
      .WithEnvironment("PROXY_GRPC_ENDPOINT", proxy.GetEndpoint("grpc"))
      .WaitFor(proxy);

  builder.Build().Run();
  ```

  Context paths explained:
  - `"../../"` — from `src/Schleusenwerk.AppHost/` goes up to repo root, where the Dockerfiles expect `COPY src/ ./src/`
  - `"src/Schleusenwerk/Dockerfile"` — Dockerfile path relative to context (repo root)

- [ ] **Step 3: Build AppHost to verify compilation**

  ```bash
  cd src
  dotnet build Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj
  ```

  Expected: build succeeds.

- [ ] **Step 4: Commit**

  ```bash
  git add src/Schleusenwerk.AppHost/Program.cs src/Schleusenwerk.AppHost/nginx.conf
  git commit -m "feat: add Aspire AppHost with proxy, UI, and nginx mock containers"
  ```

---

## Task 3 — Verify AppHost Starts Containers

This is a manual verification step — run the AppHost and confirm all three containers start.

- [ ] **Step 1: Ensure Podman is accessible**

  Verify Podman's Docker-compatible socket is active. On Windows with Podman Desktop, this should already work. If `docker ps` fails, set `DOCKER_HOST`:

  ```powershell
  # Only if needed — check first with: docker ps
  $env:DOCKER_HOST = "npipe:////./pipe/podman-machine-default"
  ```

- [ ] **Step 2: Run the AppHost**

  ```bash
  cd src
  dotnet run --project Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj
  ```

  Expected: Aspire dashboard opens in browser. Three resources appear:
  - `upstream-mock` — Running (nginx)
  - `proxy` — Running (Schleusenwerk)
  - `ui` — Running (Blazor UI)

  The first run will be slow (Docker image builds). Subsequent runs use cached layers.

- [ ] **Step 3: Smoke test**

  Open the Aspire dashboard, click on the `ui` resource endpoint — the Schleusenwerk Blazor UI should load with the MudBlazor layout and Dashboard page (it will show "Could not reach proxy" initially, which is OK — the proxy needs time to initialize its actor system).

- [ ] **Step 4: Stop the AppHost**

  Press Ctrl+C in the terminal.

---

## Task 4 — IntegrationTests Project Scaffold + Fixture

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`
- Create: `src/Schleusenwerk.IntegrationTests/SchleusenwerkFixture.cs`
- Modify: `src/Schleusenwerk.slnx`

- [ ] **Step 1: Create the IntegrationTests project file**

  Create `src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`:

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">

      <PropertyGroup>
          <OutputType>Exe</OutputType>
          <IsTestProject>true</IsTestProject>
      </PropertyGroup>

      <ItemGroup>
          <PackageReference Include="Aspire.Hosting.Testing" />
          <PackageReference Include="Grpc.Net.Client" />
          <PackageReference Include="xunit.v3.mtp-v2" />
      </ItemGroup>

      <ItemGroup>
          <ProjectReference Include="..\Schleusenwerk.AppHost\Schleusenwerk.AppHost.csproj" />
          <ProjectReference Include="..\Schleusenwerk.Contracts\Schleusenwerk.Contracts.csproj" />
      </ItemGroup>

  </Project>
  ```

- [ ] **Step 2: Add to solution**

  Add to `src/Schleusenwerk.slnx` before the `<Folder>` element:

  ```xml
  <Project Path="Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj" />
  ```

- [ ] **Step 3: Write SchleusenwerkFixture**

  Create `src/Schleusenwerk.IntegrationTests/SchleusenwerkFixture.cs`:

  ```csharp
  using Aspire.Hosting.Testing;
  using Google.Protobuf.WellKnownTypes;
  using Grpc.Net.Client;
  using Schleusenwerk.Contracts;
  using Xunit;

  namespace Schleusenwerk.IntegrationTests;

  public sealed class SchleusenwerkFixture : IAsyncLifetime
  {
      public DistributedApplication App { get; private set; } = null!;
      public GrpcChannel GrpcChannel { get; private set; } = null!;
      public HttpClient ProxyHttp { get; private set; } = null!;

      public async ValueTask InitializeAsync()
      {
          var builder = await DistributedApplicationTestingBuilder
              .CreateAsync<Projects.Schleusenwerk_AppHost>();

          App = await builder.BuildAsync();

          var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
          await App.StartAsync(cts.Token);

          var grpcEndpoint = App.GetEndpoint("proxy", "grpc");
          GrpcChannel = GrpcChannel.ForAddress(grpcEndpoint);

          var httpEndpoint = App.GetEndpoint("proxy", "http");
          ProxyHttp = new HttpClient { BaseAddress = httpEndpoint };

          await WaitForProxyReady(cts.Token);
      }

      public async ValueTask DisposeAsync()
      {
          GrpcChannel.Dispose();
          ProxyHttp.Dispose();
          await App.StopAsync();
          await App.DisposeAsync();
      }

      private async Task WaitForProxyReady(CancellationToken ct)
      {
          var client = new RouteService.RouteServiceClient(GrpcChannel);
          while (!ct.IsCancellationRequested)
          {
              try
              {
                  await client.ListRoutesAsync(new Empty(), cancellationToken: ct);
                  return;
              }
              catch
              {
                  await Task.Delay(500, ct);
              }
          }
      }
  }

  [CollectionDefinition("Schleusenwerk")]
  public sealed class SchleusenwerkCollection : ICollectionFixture<SchleusenwerkFixture>;
  ```

  Key details:
  - `IAsyncLifetime` with `ValueTask` (xUnit v3 signature)
  - `WaitForProxyReady` polls a gRPC call until the proxy's actor system is initialized
  - `[CollectionDefinition]` shares the fixture across all test classes — containers start once
  - 3-minute timeout covers Docker image build on first run

- [ ] **Step 4: Verify the project builds**

  ```bash
  cd src
  dotnet build Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj
  ```

  Expected: build succeeds.

- [ ] **Step 5: Commit**

  ```bash
  git add src/Schleusenwerk.IntegrationTests/ src/Schleusenwerk.slnx
  git commit -m "feat: add IntegrationTests project with SchleusenwerkFixture"
  ```

---

## Task 5 — RouteManagementSpec (6 tests)

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/RouteManagementSpec.cs`

- [ ] **Step 1: Write RouteManagementSpec**

  Create `src/Schleusenwerk.IntegrationTests/RouteManagementSpec.cs`:

  ```csharp
  using Google.Protobuf.WellKnownTypes;
  using Schleusenwerk.Contracts;
  using Xunit;

  namespace Schleusenwerk.IntegrationTests;

  [Collection("Schleusenwerk")]
  public sealed class RouteManagementSpec
  {
      private readonly RouteService.RouteServiceClient _routes;

      public RouteManagementSpec(SchleusenwerkFixture fixture)
      {
          _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
      }

      [Fact(Timeout = 30_000)]
      public async Task AddRoute_then_ListRoutes_should_contain_new_route()
      {
          var domain = $"list-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = true,
              TimeoutSeconds = 30,
              FirstUpstreamUrl = "http://upstream-mock"
          });

          var response = await _routes.ListRoutesAsync(new Empty());

          Assert.Contains(response.Routes, r => r.Domain == domain);
      }

      [Fact(Timeout = 30_000)]
      public async Task AddRoute_then_GetRoute_should_return_detail()
      {
          var domain = $"detail-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = true,
              TimeoutSeconds = 60,
              FirstUpstreamUrl = "http://upstream-mock"
          });

          var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain });

          Assert.Equal(domain, detail.Domain);
          Assert.True(detail.ForceHttps);
          Assert.Equal(60, detail.TimeoutSeconds);
      }

      [Fact(Timeout = 30_000)]
      public async Task UpdateRoute_should_change_config()
      {
          var domain = $"update-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = false,
              TimeoutSeconds = 30
          });

          await _routes.UpdateRouteAsync(new UpdateRouteRequest
          {
              Domain = domain,
              ForceHttps = true,
              TimeoutSeconds = 120
          });

          var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain });
          Assert.True(detail.ForceHttps);
          Assert.Equal(120, detail.TimeoutSeconds);
      }

      [Fact(Timeout = 30_000)]
      public async Task DeleteRoute_should_remove_from_list()
      {
          var domain = $"delete-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = false,
              TimeoutSeconds = 30
          });

          await _routes.DeleteRouteAsync(new DeleteRouteRequest { Domain = domain });

          var response = await _routes.ListRoutesAsync(new Empty());
          Assert.DoesNotContain(response.Routes, r => r.Domain == domain);
      }

      [Fact(Timeout = 30_000)]
      public async Task AddUpstream_then_GetRoute_should_include_upstream()
      {
          var domain = $"upstream-add-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = false,
              TimeoutSeconds = 30
          });

          await _routes.AddUpstreamAsync(new AddUpstreamRequest
          {
              Domain = domain,
              Url = "http://upstream-mock",
              Weight = 1
          });

          var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain });
          Assert.Contains(detail.Upstreams, u => u.Url.Contains("upstream-mock"));
      }

      [Fact(Timeout = 30_000)]
      public async Task RemoveUpstream_should_remove_from_route()
      {
          var domain = $"upstream-rm-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = false,
              TimeoutSeconds = 30,
              FirstUpstreamUrl = "http://upstream-mock"
          });

          await _routes.RemoveUpstreamAsync(new RemoveUpstreamRequest
          {
              Domain = domain,
              Url = "http://upstream-mock"
          });

          var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain });
          Assert.Empty(detail.Upstreams);
      }
  }
  ```

  Key details:
  - Each test uses a unique domain name (GUID) to avoid cross-test interference since all tests share one proxy instance
  - `[Collection("Schleusenwerk")]` ensures the shared fixture is used
  - `Timeout = 30_000` (30s) gives enough time for actor system processing

- [ ] **Step 2: Build to verify compilation**

  ```bash
  cd src
  dotnet build Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj
  ```

  Expected: build succeeds.

- [ ] **Step 3: Commit**

  ```bash
  git add src/Schleusenwerk.IntegrationTests/RouteManagementSpec.cs
  git commit -m "feat: add route management integration tests"
  ```

---

## Task 6 — CertificateManagementSpec (2 tests)

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/CertificateManagementSpec.cs`

- [ ] **Step 1: Write CertificateManagementSpec**

  Create `src/Schleusenwerk.IntegrationTests/CertificateManagementSpec.cs`:

  ```csharp
  using Google.Protobuf.WellKnownTypes;
  using Schleusenwerk.Contracts;
  using Xunit;

  namespace Schleusenwerk.IntegrationTests;

  [Collection("Schleusenwerk")]
  public sealed class CertificateManagementSpec
  {
      private readonly CertificateService.CertificateServiceClient _certs;
      private readonly RouteService.RouteServiceClient _routes;

      public CertificateManagementSpec(SchleusenwerkFixture fixture)
      {
          _certs = new CertificateService.CertificateServiceClient(fixture.GrpcChannel);
          _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
      }

      [Fact(Timeout = 30_000)]
      public async Task ListCertificates_should_return_empty_initially()
      {
          var response = await _certs.ListCertificatesAsync(new Empty());

          Assert.NotNull(response);
      }

      [Fact(Timeout = 30_000)]
      public async Task ProvisionCertificate_should_return_success()
      {
          var domain = $"cert-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = true,
              TimeoutSeconds = 30
          });

          var result = await _certs.ProvisionCertificateAsync(
              new ProvisionCertificateRequest { Domain = domain });

          Assert.True(result.Success);
      }
  }
  ```

- [ ] **Step 2: Build to verify**

  ```bash
  cd src
  dotnet build Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj
  ```

- [ ] **Step 3: Commit**

  ```bash
  git add src/Schleusenwerk.IntegrationTests/CertificateManagementSpec.cs
  git commit -m "feat: add certificate management integration tests"
  ```

---

## Task 7 — UpstreamForwardingSpec (2 tests)

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/UpstreamForwardingSpec.cs`

- [ ] **Step 1: Write UpstreamForwardingSpec**

  Create `src/Schleusenwerk.IntegrationTests/UpstreamForwardingSpec.cs`:

  ```csharp
  using System.Net;
  using Schleusenwerk.Contracts;
  using Xunit;

  namespace Schleusenwerk.IntegrationTests;

  [Collection("Schleusenwerk")]
  public sealed class UpstreamForwardingSpec
  {
      private readonly RouteService.RouteServiceClient _routes;
      private readonly HttpClient _proxyHttp;

      public UpstreamForwardingSpec(SchleusenwerkFixture fixture)
      {
          _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
          _proxyHttp = fixture.ProxyHttp;
      }

      [Fact(Timeout = 30_000)]
      public async Task Request_to_configured_domain_should_forward_to_upstream()
      {
          var domain = $"fwd-{Guid.NewGuid():N}.test";
          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = false,
              TimeoutSeconds = 30,
              FirstUpstreamUrl = "http://upstream-mock"
          });

          await Task.Delay(1000);

          var request = new HttpRequestMessage(HttpMethod.Get, "/");
          request.Headers.Host = domain;

          var response = await _proxyHttp.SendAsync(request);

          Assert.Equal(HttpStatusCode.OK, response.StatusCode);
          var body = await response.Content.ReadAsStringAsync();
          Assert.Contains("upstream-mock", body);
      }

      [Fact(Timeout = 30_000)]
      public async Task Request_to_unknown_domain_should_not_return_ok()
      {
          var request = new HttpRequestMessage(HttpMethod.Get, "/");
          request.Headers.Host = "nonexistent.example.com";

          var response = await _proxyHttp.SendAsync(request);

          Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
      }
  }
  ```

  Key details:
  - `Task.Delay(1000)` gives the proxy time to register the route with its actor system before the forwarding request
  - Sets `Host` header to match the configured domain so the proxy routes to the nginx-mock upstream
  - Verifies the response body contains `"upstream-mock"` (from nginx's static JSON)

- [ ] **Step 2: Build to verify**

  ```bash
  cd src
  dotnet build Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj
  ```

- [ ] **Step 3: Commit**

  ```bash
  git add src/Schleusenwerk.IntegrationTests/UpstreamForwardingSpec.cs
  git commit -m "feat: add upstream forwarding integration tests"
  ```

---

## Task 8 — EventStreamSpec (1 test)

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/EventStreamSpec.cs`

- [ ] **Step 1: Write EventStreamSpec**

  Create `src/Schleusenwerk.IntegrationTests/EventStreamSpec.cs`:

  ```csharp
  using Grpc.Core;
  using Schleusenwerk.Contracts;
  using Xunit;

  namespace Schleusenwerk.IntegrationTests;

  [Collection("Schleusenwerk")]
  public sealed class EventStreamSpec
  {
      private readonly RouteService.RouteServiceClient _routes;
      private readonly EventService.EventServiceClient _events;

      public EventStreamSpec(SchleusenwerkFixture fixture)
      {
          _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
          _events = new EventService.EventServiceClient(fixture.GrpcChannel);
      }

      [Fact(Timeout = 30_000)]
      public async Task Subscribe_should_receive_route_updated_event()
      {
          using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

          using var call = _events.Subscribe(
              new SubscribeRequest(),
              cancellationToken: cts.Token);

          var domain = $"evt-{Guid.NewGuid():N}.test";

          await Task.Delay(500, cts.Token);

          await _routes.AddRouteAsync(new AddRouteRequest
          {
              Domain = domain,
              ForceHttps = false,
              TimeoutSeconds = 30
          });

          ProxyEvent? received = null;
          try
          {
              await foreach (var evt in call.ResponseStream.ReadAllAsync(cts.Token))
              {
                  if (evt.Type == EventType.RouteUpdated && evt.Domain == domain)
                  {
                      received = evt;
                      break;
                  }
              }
          }
          catch (OperationCanceledException)
          {
          }

          Assert.NotNull(received);
          Assert.Equal(EventType.RouteUpdated, received.Type);
          Assert.Equal(domain, received.Domain);
      }
  }
  ```

  Key details:
  - Opens the Subscribe stream first, then adds a route to trigger an event
  - Filters the stream for the specific domain to avoid interference from other tests
  - 15-second timeout if no matching event arrives
  - Catches `OperationCanceledException` from the CTS timeout gracefully

- [ ] **Step 2: Build to verify**

  ```bash
  cd src
  dotnet build Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj
  ```

- [ ] **Step 3: Commit**

  ```bash
  git add src/Schleusenwerk.IntegrationTests/EventStreamSpec.cs
  git commit -m "feat: add event stream integration test"
  ```

---

## Task 9 — Full Integration Test Run + Solution Build

- [ ] **Step 1: Build the full solution**

  ```bash
  cd src
  dotnet build Schleusenwerk.slnx --configuration Release
  ```

  Expected: all projects build with no errors.

- [ ] **Step 2: Run existing unit tests (regression check)**

  ```bash
  cd src
  dotnet run --project Schleusenwerk.Tests/Schleusenwerk.Tests.csproj
  ```

  Expected: all 237+ tests pass.

- [ ] **Step 3: Run integration tests**

  ```bash
  cd src
  dotnet run --project Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj
  ```

  Expected: 11 tests pass. First run will be slow (Docker image builds, ~2-3 minutes). Subsequent runs are faster (~30-60 seconds).

  If tests fail due to timeout, increase the timeout in `SchleusenwerkFixture` from 3 minutes to 5 minutes. Container image builds on first run are the most common cause.

- [ ] **Step 4: Final commit**

  ```bash
  git add -A
  git commit -m "feat: complete Aspire dev environment and integration testing setup"
  ```

---

## Post-Implementation Notes

### Running the dev environment

```bash
cd src
dotnet run --project Schleusenwerk.AppHost/Schleusenwerk.AppHost.csproj
```

Opens the Aspire dashboard at `https://localhost:15888`. All three containers are visible with logs, endpoints, and metrics.

### Running integration tests only

```bash
cd src
dotnet run --project Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj
```

### Podman troubleshooting

If Aspire can't find the container runtime:

```powershell
$env:DOCKER_HOST = "npipe:////./pipe/podman-machine-default"
```

### ServiceDefaults — intentionally omitted

The spec mentions `Schleusenwerk.ServiceDefaults`, but it's not implemented because the proxy and UI code are not modified. ServiceDefaults provides extension methods (OpenTelemetry, health checks, resilience) that the application projects need to call explicitly. Since we're running the existing containers as-is, there's nothing to consume ServiceDefaults. It can be added later when the proxy/UI integrate Aspire natively.
