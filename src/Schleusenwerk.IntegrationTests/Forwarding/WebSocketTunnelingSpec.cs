using System.Net.WebSockets;
using System.Text;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Forwarding;

[Collection("Integration")]
public sealed class WebSocketTunnelingSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly WebSocketEchoFixture _wsEcho;

    public WebSocketTunnelingSpec(SchleusenwerkTestHost host, WebSocketEchoFixture wsEcho)
    {
        _host = host;
        _wsEcho = wsEcho;
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_upgrade_to_websocket()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("ws-upgrade");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _wsEcho.BaseUrl, webSocketEnabled: true, ct: ct);
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", domain);
        var wsUri = new UriBuilder(_host.BaseUri) { Scheme = "ws", Path = "/ws-tunnel" }.Uri;
        await ws.ConnectAsync(wsUri, ct);
        Assert.Equal(WebSocketState.Open, ws.State);
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_forward_frames_bidirectionally()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("ws-echo");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _wsEcho.BaseUrl, webSocketEnabled: true, ct: ct);
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", domain);
        var wsUri = new UriBuilder(_host.BaseUri) { Scheme = "ws", Path = "/ws-tunnel" }.Uri;
        await ws.ConnectAsync(wsUri, ct);
        var message = "hello from integration test"u8.ToArray();
        await ws.SendAsync(message, WebSocketMessageType.Text, true, ct);
        var buffer = new byte[4096];
        var received = new StringBuilder();
        while (!received.ToString().Contains("hello from integration test"))
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            received.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        Assert.Contains("hello from integration test", received.ToString());
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_handle_websocket_close()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("ws-close");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _wsEcho.BaseUrl, webSocketEnabled: true, ct: ct);
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", domain);
        var wsUri = new UriBuilder(_host.BaseUri) { Scheme = "ws", Path = "/ws-tunnel" }.Uri;
        await ws.ConnectAsync(wsUri, ct);
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "graceful close", ct);
        Assert.Equal(WebSocketState.Closed, ws.State);
    }
}
