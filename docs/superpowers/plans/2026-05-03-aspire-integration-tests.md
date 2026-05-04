# Aspire Docker Integration Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite integration tests to run both proxy and web containers in Docker via Aspire's `DistributedApplicationTestingBuilder`, with all requests going through the nginx reverse proxy.

**Architecture:** A dedicated test AppHost class inside the test project uses `AddDockerfile()` to build both containers from their Dockerfiles. The proxy runs on port 5000 (API) and 80 (proxy traffic). The web container runs nginx that reverse-proxies `/api/*` and `/hubs/*` to the proxy. All 20 tests go through the web container's nginx. The fixture starts containers once per test collection.

**Tech Stack:** Aspire.Hosting.Testing 13.2.4, xUnit v3, HttpClient, Microsoft.AspNetCore.SignalR.Client

**Spec:** `docs/superpowers/specs/2026-05-03-aspire-integration-tests-design.md`

---

## File Map

### New files
- `src/Schleusenwerk.IntegrationTests/SchleusenwerkFixture.cs` — Test fixture with Aspire container setup
- `src/Schleusenwerk.IntegrationTests/RouteManagementSpec.cs` — 7 route CRUD tests
- `src/Schleusenwerk.IntegrationTests/UpstreamManagementSpec.cs` — 3 upstream tests
- `src/Schleusenwerk.IntegrationTests/CertificateSpec.cs` — 3 certificate tests
- `src/Schleusenwerk.IntegrationTests/HealthSpec.cs` — 2 health tests
- `src/Schleusenwerk.IntegrationTests/DiscoverySpec.cs` — 1 discovery test
- `src/Schleusenwerk.IntegrationTests/SignalRSpec.cs` — 1 SignalR event test
- `src/Schleusenwerk.IntegrationTests/NginxProxySpec.cs` — 3 nginx proxy tests

### Modified files
- `src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj` — Update references

---

## Task 1: Project Setup + Fixture

**Files:**
- Modify: `src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`
- Create: `src/Schleusenwerk.IntegrationTests/SchleusenwerkFixture.cs`

- [ ] **Step 1: Update the project file**

Replace the full content of `src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <IsTestProject>true</IsTestProject>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Aspire.Hosting" />
        <PackageReference Include="Aspire.Hosting.Testing" />
        <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
        <PackageReference Include="xunit.v3.mtp-v2" />
    </ItemGroup>

</Project>
```

No project references to `Schleusenwerk`, `Schleusenwerk.Web`, or `Schleusenwerk.AppHost`. Containers are built from Dockerfiles.

- [ ] **Step 2: Create SchleusenwerkFixture**

Create `src/Schleusenwerk.IntegrationTests/SchleusenwerkFixture.cs`:

```csharp
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

public sealed class SchleusenwerkFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public Uri WebBaseUri { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync();

        var repoRoot = FindRepoRoot();

        var proxy = builder.AddDockerfile("proxy", repoRoot, "src/Schleusenwerk/Dockerfile")
            .WithHttpEndpoint(targetPort: 80, name: "http")
            .WithHttpEndpoint(targetPort: 5000, name: "api");

        builder.AddDockerfile("web", Path.Combine(repoRoot, "src", "Schleusenwerk.Web"), "Dockerfile")
            .WithHttpEndpoint(targetPort: 80, name: "http")
            .WithReference(proxy);

        App = await builder.BuildAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await App.StartAsync(cts.Token);

        WebBaseUri = App.GetEndpoint("web", "http");
        Client = new HttpClient { BaseAddress = WebBaseUri };

        await WaitForReady(cts.Token);
    }

    public HubConnection CreateHubConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(WebBaseUri, "/hubs/events"))
            .Build();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }

    private async Task WaitForReady(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await Client.GetAsync("/api/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(2000, ct);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }
}

[CollectionDefinition("Schleusenwerk")]
public sealed class SchleusenwerkCollection : ICollectionFixture<SchleusenwerkFixture>;
```

- [ ] **Step 3: Build the test project**

Run: `dotnet build src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`
Expected: Build succeeds. (Tests won't pass yet — no test files.)

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.IntegrationTests/
git commit -m "feat(tests): add Aspire Docker-based integration test fixture"
```

---

## Task 2: RouteManagementSpec

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/RouteManagementSpec.cs`

- [ ] **Step 1: Create RouteManagementSpec with 7 tests**

Create `src/Schleusenwerk.IntegrationTests/RouteManagementSpec.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class RouteManagementSpec
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _client;

    public RouteManagementSpec(SchleusenwerkFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact(Timeout = 30_000)]
    public async Task ListRoutes_should_return_empty_initially()
    {
        var response = await _client.GetAsync("/api/routes");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var routes = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(JsonValueKind.Array, routes.ValueKind);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddRoute_should_create_and_return_success()
    {
        var domain = $"add-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, forceHttps = true, timeoutSeconds = 30 });
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/routes", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(result.GetProperty("success").GetBoolean());
    }

    [Fact(Timeout = 30_000)]
    public async Task ListRoutes_should_contain_created_route()
    {
        var domain = $"list-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _client.PostAsync("/api/routes", new StringContent(body, Encoding.UTF8, "application/json"));

        var response = await _client.GetAsync("/api/routes");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var routes = JsonSerializer.Deserialize<JsonElement>(json);

        var found = routes.EnumerateArray().Any(r => r.GetProperty("domain").GetString() == domain);
        Assert.True(found);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetRoute_should_return_detail()
    {
        var domain = $"detail-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, forceHttps = true, timeoutSeconds = 60 });
        await _client.PostAsync("/api/routes", new StringContent(body, Encoding.UTF8, "application/json"));

        var response = await _client.GetAsync($"/api/routes/{domain}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(domain, detail.GetProperty("domain").GetString());
        Assert.True(detail.GetProperty("forceHttps").GetBoolean());
        Assert.Equal(60, detail.GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact(Timeout = 30_000)]
    public async Task UpdateRoute_should_change_config()
    {
        var domain = $"update-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, forceHttps = false, timeoutSeconds = 30 });
        await _client.PostAsync("/api/routes", new StringContent(body, Encoding.UTF8, "application/json"));

        var updateBody = JsonSerializer.Serialize(new { forceHttps = true, timeoutSeconds = 120 });
        await _client.PutAsync($"/api/routes/{domain}", new StringContent(updateBody, Encoding.UTF8, "application/json"));

        var response = await _client.GetAsync($"/api/routes/{domain}");
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(detail.GetProperty("forceHttps").GetBoolean());
        Assert.Equal(120, detail.GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact(Timeout = 30_000)]
    public async Task DeleteRoute_should_remove_route()
    {
        var domain = $"delete-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _client.PostAsync("/api/routes", new StringContent(body, Encoding.UTF8, "application/json"));

        await _client.DeleteAsync($"/api/routes/{domain}");

        var response = await _client.GetAsync("/api/routes");
        var json = await response.Content.ReadAsStringAsync();
        var routes = JsonSerializer.Deserialize<JsonElement>(json);

        var found = routes.EnumerateArray().Any(r => r.GetProperty("domain").GetString() == domain);
        Assert.False(found);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetRoute_should_return_404_for_unknown_domain()
    {
        var response = await _client.GetAsync("/api/routes/nonexistent.test");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Schleusenwerk.IntegrationTests/RouteManagementSpec.cs
git commit -m "feat(tests): add RouteManagementSpec with 7 Docker integration tests"
```

---

## Task 3: UpstreamManagementSpec

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/UpstreamManagementSpec.cs`

- [ ] **Step 1: Create UpstreamManagementSpec with 3 tests**

Create `src/Schleusenwerk.IntegrationTests/UpstreamManagementSpec.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class UpstreamManagementSpec
{
    private readonly HttpClient _client;

    public UpstreamManagementSpec(SchleusenwerkFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact(Timeout = 30_000)]
    public async Task AddUpstream_should_appear_in_route_detail()
    {
        var domain = $"ups-add-{Guid.NewGuid():N}.test";
        var routeBody = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _client.PostAsync("/api/routes", new StringContent(routeBody, Encoding.UTF8, "application/json"));

        var upstreamBody = JsonSerializer.Serialize(new { url = "http://backend:8080", weight = 1 });
        var addResult = await _client.PostAsync($"/api/routes/{domain}/upstreams", new StringContent(upstreamBody, Encoding.UTF8, "application/json"));
        addResult.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/routes/{domain}");
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);

        var hasUpstream = detail.GetProperty("upstreams").EnumerateArray()
            .Any(u => u.GetProperty("url").GetString()!.Contains("backend:8080"));
        Assert.True(hasUpstream);
    }

    [Fact(Timeout = 30_000)]
    public async Task RemoveUpstream_should_disappear_from_route_detail()
    {
        var domain = $"ups-rm-{Guid.NewGuid():N}.test";
        var upstreamUrl = "http://removeme:9090";
        var routeBody = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30, firstUpstreamUrl = upstreamUrl });
        await _client.PostAsync("/api/routes", new StringContent(routeBody, Encoding.UTF8, "application/json"));

        var encodedUrl = Convert.ToBase64String(Encoding.UTF8.GetBytes(upstreamUrl))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await _client.DeleteAsync($"/api/routes/{domain}/upstreams/{encodedUrl}");

        var response = await _client.GetAsync($"/api/routes/{domain}");
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);

        var upstreamCount = detail.GetProperty("upstreams").GetArrayLength();
        Assert.Equal(0, upstreamCount);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddUpstream_to_nonexistent_route_should_fail()
    {
        var upstreamBody = JsonSerializer.Serialize(new { url = "http://backend:8080", weight = 1 });
        var response = await _client.PostAsync("/api/routes/nonexistent.test/upstreams",
            new StringContent(upstreamBody, Encoding.UTF8, "application/json"));

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.False(result.GetProperty("success").GetBoolean());
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Schleusenwerk.IntegrationTests/UpstreamManagementSpec.cs
git commit -m "feat(tests): add UpstreamManagementSpec with 3 Docker integration tests"
```

---

## Task 4: CertificateSpec + HealthSpec + DiscoverySpec

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/CertificateSpec.cs`
- Create: `src/Schleusenwerk.IntegrationTests/HealthSpec.cs`
- Create: `src/Schleusenwerk.IntegrationTests/DiscoverySpec.cs`

- [ ] **Step 1: Create CertificateSpec with 3 tests**

Create `src/Schleusenwerk.IntegrationTests/CertificateSpec.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class CertificateSpec
{
    private readonly HttpClient _client;

    public CertificateSpec(SchleusenwerkFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact(Timeout = 30_000)]
    public async Task ListCertificates_should_return_empty_initially()
    {
        var response = await _client.GetAsync("/api/certificates");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var certs = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(JsonValueKind.Array, certs.ValueKind);
    }

    [Fact(Timeout = 30_000)]
    public async Task ProvisionCertificate_should_trigger_selfsigned()
    {
        var domain = $"cert-{Guid.NewGuid():N}.test";
        var routeBody = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _client.PostAsync("/api/routes", new StringContent(routeBody, Encoding.UTF8, "application/json"));

        var response = await _client.PostAsync($"/api/certificates/{domain}/provision", null);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(result.GetProperty("success").GetBoolean());
    }

    [Fact(Timeout = 30_000)]
    public async Task ListCertificates_should_contain_provisioned_cert()
    {
        var domain = $"cert-list-{Guid.NewGuid():N}.test";
        var routeBody = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _client.PostAsync("/api/routes", new StringContent(routeBody, Encoding.UTF8, "application/json"));
        await _client.PostAsync($"/api/certificates/{domain}/provision", null);

        await Task.Delay(2000);

        var response = await _client.GetAsync("/api/certificates");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var certs = JsonSerializer.Deserialize<JsonElement>(json);

        var found = certs.EnumerateArray().Any(c => c.GetProperty("domain").GetString() == domain);
        Assert.True(found);
    }
}
```

- [ ] **Step 2: Create HealthSpec with 2 tests**

Create `src/Schleusenwerk.IntegrationTests/HealthSpec.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class HealthSpec
{
    private readonly HttpClient _client;

    public HealthSpec(SchleusenwerkFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact(Timeout = 30_000)]
    public async Task GetHealth_should_return_counts()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(health.TryGetProperty("routeCount", out _));
        Assert.True(health.TryGetProperty("healthyCount", out _));
        Assert.True(health.TryGetProperty("unhealthyCount", out _));
    }

    [Fact(Timeout = 30_000)]
    public async Task GetUpstreamHealth_should_return_entries_for_domain()
    {
        var domain = $"health-{Guid.NewGuid():N}.test";
        var routeBody = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30, firstUpstreamUrl = "http://backend:8080" });
        await _client.PostAsync("/api/routes", new StringContent(routeBody, Encoding.UTF8, "application/json"));

        var response = await _client.GetAsync($"/api/health/{domain}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(domain, health.GetProperty("domain").GetString());
        Assert.True(health.TryGetProperty("upstreams", out var upstreams));
        Assert.Equal(JsonValueKind.Array, upstreams.ValueKind);
    }
}
```

- [ ] **Step 3: Create DiscoverySpec with 1 test**

Create `src/Schleusenwerk.IntegrationTests/DiscoverySpec.cs`:

```csharp
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class DiscoverySpec
{
    private readonly HttpClient _client;

    public DiscoverySpec(SchleusenwerkFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact(Timeout = 30_000)]
    public async Task ListContainers_should_return_ok()
    {
        var response = await _client.GetAsync("/api/discovery/containers");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var containers = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(JsonValueKind.Array, containers.ValueKind);
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.IntegrationTests/CertificateSpec.cs src/Schleusenwerk.IntegrationTests/HealthSpec.cs src/Schleusenwerk.IntegrationTests/DiscoverySpec.cs
git commit -m "feat(tests): add Certificate, Health, and Discovery Docker integration tests"
```

---

## Task 5: SignalRSpec + NginxProxySpec

**Files:**
- Create: `src/Schleusenwerk.IntegrationTests/SignalRSpec.cs`
- Create: `src/Schleusenwerk.IntegrationTests/NginxProxySpec.cs`

- [ ] **Step 1: Create SignalRSpec with 1 test**

Create `src/Schleusenwerk.IntegrationTests/SignalRSpec.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class SignalRSpec
{
    private readonly SchleusenwerkFixture _fixture;

    public SignalRSpec(SchleusenwerkFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_receive_event_when_route_created()
    {
        await using var hub = _fixture.CreateHubConnection();

        var eventReceived = new TaskCompletionSource<JsonElement>();
        hub.On<JsonElement>("OnProxyEvent", evt =>
        {
            eventReceived.TrySetResult(evt);
        });

        await hub.StartAsync();

        var domain = $"signalr-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _fixture.Client.PostAsync("/api/routes",
            new StringContent(body, Encoding.UTF8, "application/json"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => eventReceived.TrySetCanceled());

        var evt = await eventReceived.Task;
        Assert.Equal("RouteUpdated", evt.GetProperty("type").GetString());
    }
}
```

- [ ] **Step 2: Create NginxProxySpec with 3 tests**

Create `src/Schleusenwerk.IntegrationTests/NginxProxySpec.cs`:

```csharp
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class NginxProxySpec
{
    private readonly HttpClient _client;

    public NginxProxySpec(SchleusenwerkFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact(Timeout = 30_000)]
    public async Task Api_routes_should_be_proxied_through_nginx()
    {
        var response = await _client.GetAsync("/api/routes");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(Timeout = 30_000)]
    public async Task SignalR_hub_should_be_reachable_through_nginx()
    {
        var response = await _client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", null);

        Assert.True(response.IsSuccessStatusCode,
            $"SignalR negotiate returned {response.StatusCode}");
    }

    [Fact(Timeout = 30_000)]
    public async Task Root_should_return_index_html()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<div id=\"app\">", html);
    }
}
```

- [ ] **Step 3: Build all tests**

Run: `dotnet build src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.IntegrationTests/SignalRSpec.cs src/Schleusenwerk.IntegrationTests/NginxProxySpec.cs
git commit -m "feat(tests): add SignalR and nginx proxy Docker integration tests"
```

---

## Task 6: Run Tests + Fix Issues

**Files:**
- Potentially modify any test file or fixture to fix runtime issues

- [ ] **Step 1: Run integration tests**

Run: `dotnet run --project src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`

Expected: Docker images are built, containers start, tests run through nginx. This will take several minutes on first run (Docker image build).

- [ ] **Step 2: Fix any issues**

Common issues to watch for:
- **Aspire `AddDockerfile` API differences**: The exact API may differ between Aspire versions. Check if `AddDockerfile` takes `(name, contextPath, dockerfilePath)` or `(name, contextPath).WithDockerfile(path)`. Adapt the fixture accordingly.
- **nginx DNS resolution**: The nginx config uses `proxy` as the upstream hostname. In Aspire's Docker network, containers are reachable by their resource name. If `proxy` doesn't resolve, the nginx proxy_pass will fail — check container networking.
- **Port mapping**: Aspire assigns random host ports. The tests use `App.GetEndpoint("web", "http")` which returns the mapped host URL. This should work correctly.
- **Build context paths**: The proxy Dockerfile expects `COPY src/ ./src/` from the repo root. The `FindRepoRoot()` helper provides this path. If it doesn't work, check the path resolution.
- **Timeout**: First run builds Docker images — increase timeout if needed.

- [ ] **Step 3: Verify all 20 tests pass**

Run: `dotnet run --project src/Schleusenwerk.IntegrationTests/Schleusenwerk.IntegrationTests.csproj`
Expected: 20 tests pass.

- [ ] **Step 4: Commit any fixes**

```bash
git add src/Schleusenwerk.IntegrationTests/
git commit -m "fix(tests): resolve Docker integration test runtime issues"
```
