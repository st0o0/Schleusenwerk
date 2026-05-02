# Proxy Dispatcher Design

**Date:** 2026-05-02  
**Status:** Approved

## Context

The current `ProxyRequestHandler` middleware intercepts all HTTP requests and handles domain resolution, upstream selection, load balancing, and forwarding in one class. This is hard to unit test and structurally implicit (invisible in the route table).

## Goal

Replace the middleware with an explicit `MapFallback` endpoint backed by a lean `ProxyDispatcher` service. Separate load balancing (which upstream) from forwarding (the actual HTTP call). Keep async I/O for natural parallelism — no concurrency limiter needed.

## Architecture

### Entry Point

```csharp
// SchleusenwerkApplicationSetup.cs
app.UseWebSockets();
app.MapFallback(async (HttpContext ctx, IProxyDispatcher dispatcher, CancellationToken ct) =>
    await dispatcher.HandleAsync(ctx, ct));
```

`ProxyRequestHandler` is deleted. The endpoint is visible in the route table and testable via standard `WebApplicationFactory<T>` patterns.

### ProxyDispatcher

```csharp
public sealed class ProxyDispatcher : IProxyDispatcher
{
    public async Task HandleAsync(HttpContext context, CancellationToken ct)
    {
        var route = await _router.ResolveAsync(context.Request.Host.Host, ct);
        if (route is null) { context.Response.StatusCode = 502; return; }

        var upstream = _selector.Select(route);

        if (context.WebSockets.IsWebSocketRequest)
            await _webSocketTunnel.TunnelAsync(context, upstream, ct);
        else
            await _pipeline.ForwardAsync(context, upstream, route.Config, ct);
    }
}
```

Single responsibility: dispatch. No routing state, no load balancing logic, no header manipulation (that stays in `RequestForwardingPipeline`).

### IUpstreamSelector

Extracted from `ProxyRequestHandler`. Holds round-robin counters per domain. Stateless from the caller's perspective.

```csharp
public interface IUpstreamSelector
{
    UpstreamTarget Select(RouteDefinition route);
}
```

Registered as singleton. Replaces the `ConcurrentDictionary<DomainName, int>` that lived in the middleware.

### Parallelism

No limiter. Kestrel and TurboHTTP handle concurrent requests naturally via async I/O. The `ProxyDispatcher` is called once per request on the thread pool — parallelism is inherent.

## Data Flow

```
Client Request
  → Kestrel
  → MapFallback → ProxyDispatcher
      → DomainRouterActor.Ask(ResolveUpstream) → RouteDefinition
      → IUpstreamSelector.Select → UpstreamTarget
      → WebSocket? → WebSocketTunnel.TunnelAsync
      → HTTP?     → RequestForwardingPipeline.ForwardAsync (TurboHTTP)
  → Response to client
```

## Error Handling

| Condition | Behaviour |
|-----------|-----------|
| Domain not found | 502, logged |
| No healthy upstreams | 502, logged |
| Upstream timeout | 504 (handled in `RequestForwardingPipeline`) |
| Upstream connection refused | 502 (handled in `RequestForwardingPipeline`) |
| `RequestAborted` | Request cancelled cleanly via `CancellationToken` |

## Testing

### Unit Tests

- `ProxyDispatcher`: mock `IDomainRouterActor`, mock `IUpstreamSelector`, mock `RequestForwardingPipeline` — verify dispatch logic (WebSocket vs HTTP, 502 on no route)
- `UpstreamSelector`: pure logic test — assert round-robin distribution over the upstreams in the `RouteDefinition` (health filtering is `DomainRouterActor`'s responsibility, not `UpstreamSelector`'s)

### Integration Tests

- `WebApplicationFactory<T>` with a stub upstream HTTP server
- No middleware mock needed — test hits the real `MapFallback` endpoint

## Components Affected

| Component | Change |
|-----------|--------|
| `ProxyRequestHandler` | Deleted |
| `SchleusenwerkApplicationSetup` | `UseProxyRequestHandler()` → `MapFallback(...)` |
| `SchleusenwerkServicesSetup` | Register `IProxyDispatcher`, `IUpstreamSelector` |
| `ProxyDispatcher` | New class |
| `IProxyDispatcher` | New interface |
| `UpstreamSelector` | New class (extracted from `ProxyRequestHandler`) |
| `IUpstreamSelector` | New interface |
| `RequestForwardingPipeline` | No change |
| `WebSocketTunnel` | No change |
| `HeaderManipulationFilter` | No change |
| `DomainRouterActor` | No change |
