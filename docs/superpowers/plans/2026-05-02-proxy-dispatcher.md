# Proxy Dispatcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ProxyRequestHandler` middleware with an explicit `MapFallback` endpoint backed by a lean `ProxyDispatcher` service, making the proxy entry point visible in the route table and unit-testable without middleware infrastructure.

**Architecture:** `app.MapFallback` delegates to `IProxyDispatcher`. `ProxyDispatcher` is a singleton DI service that resolves the domain via `DomainRouterActor`, then forwards via `RequestForwardingPipeline` (HTTP) or `WebSocketTunnel` (WS). Load balancing is already handled by `LoadBalancerActor` (Akka `RoundRobinGroup`) inside `DomainRouterActor` — no new selector needed.

**Tech Stack:** ASP.NET Core Minimal API (`MapFallback`), Akka.NET (`IRequiredActor<T>`), TurboHTTP (`RequestForwardingPipeline`), xUnit v3, Akka.TestKit

> **Note on spec vs reality:** The design spec proposed `IUpstreamSelector` as a new piece. On reading the code, `DomainRouterActor` already delegates to `LoadBalancerActor` (Akka `RoundRobinGroup`) and returns a single `UpstreamResolved(Target, Config)`. No selector is needed.

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/Schleusenwerk/Forwarding/IProxyDispatcher.cs` | Public contract |
| Create | `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs` | Dispatch logic |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs` | Wire `MapFallback` |
| Modify | `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs` | Register `IProxyDispatcher` |
| Delete | `src/Schleusenwerk/Forwarding/ProxyRequestHandler.cs` | Replaced |
| Create | `src/Schleusenwerk.Tests/Forwarding/ProxyDispatcherSpec.cs` | Tests for dispatcher |
| Delete | `src/Schleusenwerk.Tests/Forwarding/ProxyRequestHandlerSpec.cs` | Replaced |

---

## Task 1: Create `IProxyDispatcher`

**Files:**
- Create: `src/Schleusenwerk/Forwarding/IProxyDispatcher.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Schleusenwerk.Forwarding;

internal interface IProxyDispatcher
{
    Task HandleAsync(HttpContext context, CancellationToken ct);
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
dotnet build --configuration Release ./src/Schleusenwerk.slnx
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Forwarding/IProxyDispatcher.cs
git commit -m "feat(Forwarding): add IProxyDispatcher interface"
```

---

## Task 2: Create `ProxyDispatcher`

**Files:**
- Create: `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs`

- [ ] **Step 1: Write the implementation**

The logic mirrors `ProxyRequestHandler.InvokeAsync` + `HandleResolvedRoute`, minus the `RequestDelegate next` (it's a terminal handler):

```csharp
using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Forwarding;

internal sealed class ProxyDispatcher : IProxyDispatcher
{
    private readonly IActorRef _domainRouter;
    private readonly RequestForwardingPipeline _pipeline;
    private readonly HeaderManipulationFilter _headerFilter;
    private readonly WebSocketTunnel _webSocketTunnel;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);

    public ProxyDispatcher(
        IRequiredActor<DomainRouterActor> domainRouterProvider,
        RequestForwardingPipeline pipeline,
        HeaderManipulationFilter headerFilter,
        WebSocketTunnel webSocketTunnel)
    {
        _domainRouter = domainRouterProvider.ActorRef;
        _pipeline = pipeline;
        _headerFilter = headerFilter;
        _webSocketTunnel = webSocketTunnel;
    }

    public async Task HandleAsync(HttpContext context, CancellationToken ct)
    {
        var host = context.Request.Host.Host;

        if (string.IsNullOrEmpty(host))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var response = await _domainRouter.Ask<object>(
            new ResolveUpstream(host),
            AskTimeout,
            ct);

        switch (response)
        {
            case UpstreamResolved resolved:
                await HandleResolvedRoute(context, resolved.Target, resolved.Config, ct);
                break;

            case UpstreamNotFound:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                break;
        }
    }

    private async Task HandleResolvedRoute(
        HttpContext context,
        UpstreamTarget upstream,
        DomainConfig config,
        CancellationToken ct)
    {
        if (ShouldRedirectToHttps(context, config))
        {
            RedirectToHttps(context, config);
            return;
        }

        if (WebSocketTunnel.IsWebSocketUpgrade(context.Request))
        {
            await _webSocketTunnel.TunnelAsync(context, upstream, config, ct);
            return;
        }

        await _pipeline.ForwardAsync(context, upstream, config, _headerFilter);
    }

    private static bool ShouldRedirectToHttps(HttpContext context, DomainConfig config)
    {
        return config.ForceHttps
               && config.HttpRedirect != RedirectMode.None
               && string.Equals(context.Request.Scheme, "http", StringComparison.OrdinalIgnoreCase);
    }

    private static void RedirectToHttps(HttpContext context, DomainConfig config)
    {
        var request = context.Request;
        var httpsUrl = $"https://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
        context.Response.StatusCode = (int)config.HttpRedirect;
        context.Response.Headers.Location = httpsUrl;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build --configuration Release ./src/Schleusenwerk.slnx
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Forwarding/ProxyDispatcher.cs
git commit -m "feat(Forwarding): add ProxyDispatcher service"
```

---

## Task 3: Write `ProxyDispatcherSpec` (tests first, handler second)

**Files:**
- Create: `src/Schleusenwerk.Tests/Forwarding/ProxyDispatcherSpec.cs`

The `RecordingTurboHttpClient` and `StubTurboHttpClientFactory` helpers are copied from the old `ProxyRequestHandlerSpec` (they will be deleted in Task 5).

- [ ] **Step 1: Write the spec file**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http;
using Schleusenwerk.Forwarding;
using Schleusenwerk.LoadBalancing;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using TurboHTTP;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class ProxyDispatcherSpec : TestKit
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(3);
    private readonly ActorRegistry _registry;

    public ProxyDispatcherSpec()
    {
        _registry = ActorRegistry.For(Sys);
    }

    private IActorRef CreateRouter()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-{Guid.NewGuid():N}");
        _registry.Register<EventHub>(hub, overwrite: true);

        var router = Sys.ActorOf(
            Props.Create(() => new DomainRouterActor(
                upstreams => Props.Create(() => new LoadBalancerActor(upstreams)))),
            $"router-{Guid.NewGuid():N}");
        _registry.Register<DomainRouterActor>(router, overwrite: true);

        return router;
    }

    private static RouteDefinition CreateRoute(
        string domain,
        RedirectMode redirect = RedirectMode.None,
        bool forceHttps = false,
        params string[] upstreams)
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse(domain),
            HttpRedirect = redirect,
            ForceHttps = forceHttps,
        };
        var targets = upstreams.Select(u => UpstreamTarget.Create(u)).ToList();
        return RouteDefinition.Create(config, targets);
    }

    private ProxyDispatcher CreateDispatcher(
        IActorRef router,
        RecordingTurboHttpClient? recordingClient = null)
    {
        var client = recordingClient ?? new RecordingTurboHttpClient();
        var factory = new StubTurboHttpClientFactory(client);
        var pipeline = new RequestForwardingPipeline(factory);
        var headerFilter = new HeaderManipulationFilter();
        var webSocketTunnel = new WebSocketTunnel();

        return new ProxyDispatcher(
            new RequiredActor<DomainRouterActor>(_registry),
            pipeline,
            headerFilter,
            webSocketTunnel);
    }

    private static DefaultHttpContext CreateHttpContext(
        string host,
        string path = "/",
        string scheme = "http",
        string method = "GET",
        string? queryString = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.Method = method;
        if (queryString is not null)
        {
            context.Request.QueryString = new QueryString(queryString);
        }
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_return_404_when_domain_not_configured()
    {
        var router = CreateRouter();
        var dispatcher = CreateDispatcher(router);
        var context = CreateHttpContext("unknown.example.com", "/test");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_forward_request_when_domain_is_configured()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var dispatcher = CreateDispatcher(router, recordingClient);
        var context = CreateHttpContext("example.com", "/api/data");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Single(recordingClient.SentRequests);
        Assert.Contains("backend", recordingClient.SentRequests[0].RequestUri!.Host);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_redirect_http_to_https_with_301_when_configured()
    {
        var router = CreateRouter();
        var route = CreateRoute("secure.example.com",
            redirect: RedirectMode.PermanentRedirect,
            forceHttps: true,
            upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("secure.example.com"), AskTimeout);

        var dispatcher = CreateDispatcher(router);
        var context = CreateHttpContext("secure.example.com", "/page", queryString: "?q=1");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        Assert.Equal("https://secure.example.com/page?q=1", context.Response.Headers.Location.ToString());
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_redirect_with_307_when_configured()
    {
        var router = CreateRouter();
        var route = CreateRoute("temp.example.com",
            redirect: RedirectMode.TemporaryRedirect,
            forceHttps: true,
            upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("temp.example.com"), AskTimeout);

        var dispatcher = CreateDispatcher(router);
        var context = CreateHttpContext("temp.example.com", "/api");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status307TemporaryRedirect, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_not_redirect_when_already_https()
    {
        var router = CreateRouter();
        var route = CreateRoute("secure.example.com",
            redirect: RedirectMode.PermanentRedirect,
            forceHttps: true,
            upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("secure.example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var dispatcher = CreateDispatcher(router, recordingClient);
        var context = CreateHttpContext("secure.example.com", "/page", scheme: "https");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Single(recordingClient.SentRequests);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_round_robin_across_multiple_upstreams()
    {
        var router = CreateRouter();
        var route = CreateRoute("lb.example.com",
            upstreams: ["http://a:8080", "http://b:8080", "http://c:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("lb.example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var dispatcher = CreateDispatcher(router, recordingClient);

        for (var i = 0; i < 6; i++)
        {
            var context = CreateHttpContext("lb.example.com", "/test");
            await dispatcher.HandleAsync(context, CancellationToken.None);
        }

        Assert.Equal(6, recordingClient.SentRequests.Count);
        var hosts = recordingClient.SentRequests.Select(r => r.RequestUri!.Host).ToList();
        Assert.Equal(2, hosts.Count(h => h == "a"));
        Assert.Equal(2, hosts.Count(h => h == "b"));
        Assert.Equal(2, hosts.Count(h => h == "c"));
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_apply_header_manipulation_filter()
    {
        var router = CreateRouter();
        var route = CreateRoute("example.com", upstreams: ["http://backend:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient(additionalResponseHeaders: new Dictionary<string, string>
        {
            ["Server"] = "hidden-upstream",
            ["X-Powered-By"] = "SomeFramework",
        });
        var dispatcher = CreateDispatcher(router, recordingClient);
        var context = CreateHttpContext("example.com", "/test");

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Server"));
        Assert.False(context.Response.Headers.ContainsKey("X-Powered-By"));
        Assert.True(context.Response.Headers.ContainsKey("Via"));
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_return_400_when_host_header_is_empty()
    {
        var router = CreateRouter();
        var dispatcher = CreateDispatcher(router);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(string.Empty);
        context.Request.Path = "/test";
        context.Request.Method = "GET";

        await dispatcher.HandleAsync(context, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Dispatcher_should_forward_single_upstream_without_round_robin()
    {
        var router = CreateRouter();
        var route = CreateRoute("single.example.com", upstreams: ["http://only:8080"]);
        router.Tell(new UpdateRoutes([route]));
        await router.Ask<UpstreamResolved>(new ResolveUpstream("single.example.com"), AskTimeout);

        var recordingClient = new RecordingTurboHttpClient();
        var dispatcher = CreateDispatcher(router, recordingClient);

        for (var i = 0; i < 3; i++)
        {
            var context = CreateHttpContext("single.example.com", "/test");
            await dispatcher.HandleAsync(context, CancellationToken.None);
        }

        Assert.Equal(3, recordingClient.SentRequests.Count);
        Assert.All(recordingClient.SentRequests, r => Assert.Equal("only", r.RequestUri!.Host));
    }

    internal sealed class RecordingTurboHttpClient : ITurboHttpClient
    {
        private readonly Dictionary<string, string>? _additionalResponseHeaders;

        public RecordingTurboHttpClient(Dictionary<string, string>? additionalResponseHeaders = null)
        {
            _additionalResponseHeaders = additionalResponseHeaders;
        }

        public List<HttpRequestMessage> SentRequests { get; } = [];

        public Uri? BaseAddress { get; set; }
        public HttpRequestHeaders DefaultRequestHeaders => new HttpRequestMessage().Headers;
        public Version DefaultRequestVersion { get; set; } = HttpVersion.Version11;
        public HttpVersionPolicy DefaultVersionPolicy { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public long MaxResponseContentBufferSize { get; set; }
        public ChannelWriter<HttpRequestMessage> Requests => Channel.CreateUnbounded<HttpRequestMessage>().Writer;
        public ChannelReader<HttpResponseMessage> Responses => Channel.CreateUnbounded<HttpResponseMessage>().Reader;

        public void CancelPendingRequests() { }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (_additionalResponseHeaders is not null)
            {
                foreach (var header in _additionalResponseHeaders)
                {
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            return Task.FromResult(response);
        }

        public void Dispose() { }
    }

    private sealed class StubTurboHttpClientFactory : ITurboHttpClientFactory
    {
        private readonly ITurboHttpClient _client;

        public StubTurboHttpClientFactory(ITurboHttpClient client)
        {
            _client = client;
        }

        public ITurboHttpClient CreateClient(string name) => _client;
    }
}
```

- [ ] **Step 2: Build (tests won't compile yet — `ProxyDispatcher` is `internal`, but test project needs access)**

Check if the test project already has `InternalsVisibleTo`:

```bash
grep -r "InternalsVisibleTo" src/Schleusenwerk/
```

If missing, add to `src/Schleusenwerk/Schleusenwerk.csproj`:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
    <_Parameter1>Schleusenwerk.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

Then build:

```bash
dotnet build --configuration Release ./src/Schleusenwerk.slnx
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the new tests — they should FAIL because `ProxyDispatcher` doesn't exist yet as a registered type (but the class exists from Task 2, so tests should actually run)**

```bash
dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Forwarding.ProxyDispatcherSpec"
```
Expected: All tests PASS (the implementation already exists from Task 2).

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.Tests/Forwarding/ProxyDispatcherSpec.cs src/Schleusenwerk/Schleusenwerk.csproj
git commit -m "test(Forwarding): add ProxyDispatcherSpec"
```

---

## Task 4: Wire `MapFallback` and register `IProxyDispatcher`

**Files:**
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`

- [ ] **Step 1: Register `IProxyDispatcher` in `SchleusenwerkServicesSetup`**

Add one line after the `WebSocketTunnel` registration:

```csharp
services.AddSingleton<WebSocketTunnel>();
services.AddSingleton<IProxyDispatcher, ProxyDispatcher>();  // add this line
services.AddSingleton<IConfigurationService, ConfigurationService>();
```

- [ ] **Step 2: Replace `UseProxyRequestHandler()` with `MapFallback` in `SchleusenwerkApplicationSetup`**

Replace the entire `SetupApplication` body:

```csharp
protected override void SetupApplication(WebApplication app)
{
    app.MapGet("/health", () => Results.Ok("healthy"));
    app.Use(HttpsRedirectionMiddleware);
    app.UseWebSockets();
    app.MapFallback(async (HttpContext ctx, IProxyDispatcher dispatcher, CancellationToken ct) =>
        await dispatcher.HandleAsync(ctx, ct));
}
```

Remove the `using Schleusenwerk.Forwarding;` import if `UseProxyRequestHandler` was the only usage — check and clean up.

- [ ] **Step 3: Build**

```bash
dotnet build --configuration Release ./src/Schleusenwerk.slnx
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs
git commit -m "feat(Startup): wire MapFallback endpoint, register IProxyDispatcher"
```

---

## Task 5: Delete `ProxyRequestHandler` and its tests

**Files:**
- Delete: `src/Schleusenwerk/Forwarding/ProxyRequestHandler.cs`
- Delete: `src/Schleusenwerk.Tests/Forwarding/ProxyRequestHandlerSpec.cs`

- [ ] **Step 1: Delete the middleware**

```bash
rm src/Schleusenwerk/Forwarding/ProxyRequestHandler.cs
```

- [ ] **Step 2: Delete the old spec**

```bash
rm src/Schleusenwerk.Tests/Forwarding/ProxyRequestHandlerSpec.cs
```

- [ ] **Step 3: Build — verify no dangling references**

```bash
dotnet build --configuration Release ./src/Schleusenwerk.slnx
```
Expected: Build succeeded, 0 errors. If you see "UseProxyRequestHandler not found", check `SchleusenwerkApplicationSetup.cs` — the import may still reference the old extension method.

- [ ] **Step 4: Run all tests**

```bash
dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj
```
Expected: All tests pass, 0 failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(Forwarding): remove ProxyRequestHandler middleware and its tests"
```

---

## Task 6: Final verification

- [ ] **Step 1: Full build + tests**

```bash
dotnet build --configuration Release ./src/Schleusenwerk.slnx && dotnet test --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj
```
Expected: Build succeeded, all tests pass.

- [ ] **Step 2: Confirm route table is explicit**

The proxy entry point is now visible in the route table. No middleware intercepts unknown routes silently. `MapFallback` is the last-resort handler for all unmatched paths.

- [ ] **Step 3: Commit if any cleanup**

```bash
git add -A
git commit -m "chore: proxy dispatcher migration complete"
```
