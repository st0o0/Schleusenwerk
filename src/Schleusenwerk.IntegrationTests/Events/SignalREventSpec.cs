using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Events;

[Collection("Integration")]
public sealed class SignalREventSpec
{
    private readonly SchleusenwerkTestHost _host;
    public SignalREventSpec(SchleusenwerkTestHost host) => _host = host;

    [Fact(Timeout = 30_000)]
    public async Task Should_receive_route_created_event()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var hub = _host.CreateHubConnection();
        var eventReceived = new TaskCompletionSource<JsonElement>();
        hub.On<JsonElement>("OnProxyEvent", evt => eventReceived.TrySetResult(evt));
        await hub.StartAsync(ct);
        var domain = TestHelper.UniqueDomain("signalr-create");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, "http://backend:8080", ct: ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => eventReceived.TrySetCanceled());
        var evt = await eventReceived.Task;
        Assert.Equal("RouteUpdated", evt.GetProperty("type").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_receive_route_deleted_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("signalr-delete");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, "http://backend:8080", ct: ct);
        await using var hub = _host.CreateHubConnection();
        var eventReceived = new TaskCompletionSource<JsonElement>();
        hub.On<JsonElement>("OnProxyEvent", evt =>
        {
            if (evt.GetProperty("type").GetString() == "RouteRemoved") eventReceived.TrySetResult(evt);
        });
        await hub.StartAsync(ct);
        await TestHelper.RemoveRouteAsync(_host.Client, domain, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => eventReceived.TrySetCanceled());
        var evt = await eventReceived.Task;
        Assert.Equal("RouteRemoved", evt.GetProperty("type").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_reconnect_after_disconnect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var hub = _host.CreateHubConnection();
        await hub.StartAsync(ct);
        Assert.Equal(HubConnectionState.Connected, hub.State);
        await hub.StopAsync(ct);
        Assert.Equal(HubConnectionState.Disconnected, hub.State);
        await hub.StartAsync(ct);
        Assert.Equal(HubConnectionState.Connected, hub.State);
    }
}
