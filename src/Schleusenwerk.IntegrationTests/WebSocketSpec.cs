using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class WebSocketSpec : IAsyncLifetime
{
    private readonly SchleusenwerkFixture _fixture;
    private IContainer _wsEcho = null!;
    private string _wsUpstreamUrl = null!;
    private readonly string _wsDomain = $"ws-{Guid.NewGuid():N}.test";
    private readonly string _noWsDomain = $"nows-{Guid.NewGuid():N}.test";

    public WebSocketSpec(SchleusenwerkFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        _wsEcho = new ContainerBuilder()
            .WithImage("jmalloc/echo-server")
            .WithPortBinding(8080, true)
            .WithEnvironment("PORT", "8080")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080)))
            .Build();

        await _wsEcho.StartAsync();

        var hostPort = _wsEcho.GetMappedPublicPort(8080);
        _wsUpstreamUrl = $"http://host.docker.internal:{hostPort}";
    }

    public async ValueTask DisposeAsync()
    {
        var ct = CancellationToken.None;
        await _fixture.Client.DeleteAsync($"/api/routes/{_wsDomain}", ct);
        await _fixture.Client.DeleteAsync($"/api/routes/{_noWsDomain}", ct);
        await _wsEcho.StopAsync();
        await _wsEcho.DisposeAsync();
    }

    [Fact(Timeout = 60_000)]
    public async Task Route_should_include_websocket_enabled_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(_wsDomain, webSocketEnabled: true, ct: ct);

        var response = await _fixture.Client.GetAsync($"/api/routes/{_wsDomain}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(detail.GetProperty("webSocketEnabled").GetBoolean());
    }

    [Fact(Timeout = 60_000)]
    public async Task Route_should_default_websocket_to_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = $"ws-default-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _fixture.Client.PostAsync(
            "/api/routes",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);

        try
        {
            var response = await _fixture.Client.GetAsync($"/api/routes/{domain}", ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            var detail = JsonSerializer.Deserialize<JsonElement>(json);
            Assert.False(detail.GetProperty("webSocketEnabled").GetBoolean());
        }
        finally
        {
            await _fixture.Client.DeleteAsync($"/api/routes/{domain}", ct);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task UpdateRoute_should_toggle_websocket()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(_wsDomain, webSocketEnabled: false, ct: ct);

        var updateBody = JsonSerializer.Serialize(new { forceHttps = false, webSocketEnabled = true, timeoutSeconds = 30 });
        await _fixture.Client.PutAsync(
            $"/api/routes/{_wsDomain}",
            new StringContent(updateBody, Encoding.UTF8, "application/json"), ct);

        var response = await _fixture.Client.GetAsync($"/api/routes/{_wsDomain}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(detail.GetProperty("webSocketEnabled").GetBoolean());
    }

    [Fact(Timeout = 60_000)]
    public async Task WebSocket_should_connect_when_enabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(_wsDomain, webSocketEnabled: true, ct: ct);

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", _wsDomain);

        var wsUri = BuildWebSocketUri(_fixture.ApiBaseUri, "/.ws");
        await ws.ConnectAsync(wsUri, ct);

        Assert.Equal(WebSocketState.Open, ws.State);

        var message = "hello from integration test"u8.ToArray();
        await ws.SendAsync(message, WebSocketMessageType.Text, true, ct);

        var buffer = new byte[1024];
        var result = await ws.ReceiveAsync(buffer, ct);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);

        var echo = Encoding.UTF8.GetString(buffer, 0, result.Count);
        Assert.Contains("hello from integration test", echo);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
    }

    [Fact(Timeout = 60_000)]
    public async Task WebSocket_should_be_rejected_when_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(_noWsDomain, webSocketEnabled: false, ct: ct);

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", _noWsDomain);

        var wsUri = BuildWebSocketUri(_fixture.ApiBaseUri, "/.ws");

        var ex = await Assert.ThrowsAsync<WebSocketException>(
            () => ws.ConnectAsync(wsUri, ct));

        Assert.Contains("403", ex.Message);
    }

    [Fact(Timeout = 60_000)]
    public async Task ListRoutes_should_include_websocket_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(_wsDomain, webSocketEnabled: true, ct: ct);

        var response = await _fixture.Client.GetAsync("/api/routes", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var routes = JsonSerializer.Deserialize<JsonElement>(json);

        var route = routes.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("domain").GetString() == _wsDomain);

        Assert.NotEqual(default, route);
        Assert.True(route.GetProperty("webSocketEnabled").GetBoolean());
    }

    private async Task RegisterRoute(string domain, bool webSocketEnabled = false, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            domain,
            forceHttps = false,
            webSocketEnabled,
            timeoutSeconds = 30,
            tlsMode = "selfsigned",
            firstUpstreamUrl = _wsUpstreamUrl,
        });

        var response = await _fixture.Client.PostAsync(
            "/api/routes",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(result.GetProperty("success").GetBoolean(),
            $"Route registration failed: {result}");
    }

    private static Uri BuildWebSocketUri(Uri httpBase, string path)
    {
        var builder = new UriBuilder(httpBase)
        {
            Scheme = httpBase.Scheme == "https" ? "wss" : "ws",
            Path = path,
        };
        return builder.Uri;
    }
}
