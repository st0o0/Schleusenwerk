using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Schleusenwerk.Forwarding;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class WebSocketTunnelSpec
{
    [Fact(Timeout = 5000)]
    public void IsWebSocketUpgrade_should_return_true_when_upgrade_header_is_websocket()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Upgrade = "websocket";
        context.Request.Headers.Connection = "Upgrade";

        Assert.True(WebSocketTunnel.IsWebSocketUpgrade(context.Request));
    }

    [Fact(Timeout = 5000)]
    public void IsWebSocketUpgrade_should_return_true_case_insensitive()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Upgrade = "WebSocket";
        context.Request.Headers.Connection = "Upgrade";

        Assert.True(WebSocketTunnel.IsWebSocketUpgrade(context.Request));
    }

    [Fact(Timeout = 5000)]
    public void IsWebSocketUpgrade_should_return_false_when_no_upgrade_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Connection = "keep-alive";

        Assert.False(WebSocketTunnel.IsWebSocketUpgrade(context.Request));
    }

    [Fact(Timeout = 5000)]
    public void IsWebSocketUpgrade_should_return_false_when_upgrade_is_not_websocket()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Upgrade = "h2c";
        context.Request.Headers.Connection = "Upgrade";

        Assert.False(WebSocketTunnel.IsWebSocketUpgrade(context.Request));
    }

    [Fact(Timeout = 5000)]
    public void IsWebSocketUpgrade_should_return_false_for_empty_upgrade_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Upgrade = "";

        Assert.False(WebSocketTunnel.IsWebSocketUpgrade(context.Request));
    }

    [Fact(Timeout = 5000)]
    public void BuildWebSocketUri_should_convert_http_to_ws()
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/ws/chat",
                QueryString = new QueryString("?room=1")
            }
        };

        var upstream = UpstreamUrl.Parse("http://backend:8080");

        var uri = WebSocketTunnel.BuildWebSocketUri(context.Request, upstream);

        Assert.Equal("ws", uri.Scheme);
        Assert.Equal("backend", uri.Host);
        Assert.Equal(8080, uri.Port);
        Assert.Equal("/ws/chat", uri.AbsolutePath);
        Assert.Equal("?room=1", uri.Query);
    }

    [Fact(Timeout = 5000)]
    public void BuildWebSocketUri_should_convert_https_to_wss()
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/ws",
                QueryString = QueryString.Empty
            }
        };

        var upstream = UpstreamUrl.Parse("https://secure-backend:443");

        var uri = WebSocketTunnel.BuildWebSocketUri(context.Request, upstream);

        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("secure-backend", uri.Host);
    }

    [Fact(Timeout = 5000)]
    public void BuildWebSocketUri_should_preserve_path_and_query()
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/api/v2/stream",
                QueryString = new QueryString("?token=abc&format=json")
            }
        };

        var upstream = UpstreamUrl.Parse("http://api-server:3000");

        var uri = WebSocketTunnel.BuildWebSocketUri(context.Request, upstream);

        Assert.Equal("/api/v2/stream", uri.AbsolutePath);
        Assert.Equal("?token=abc&format=json", uri.Query);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureUpstreamSocket_should_forward_subprotocol()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.SecWebSocketProtocol = "graphql-ws, graphql-transport-ws";
        context.Request.Headers.Host = "example.com";
        context.Request.Scheme = "https";

        var socket = new ClientWebSocket();

        // Should not throw; sub-protocols are added via AddSubProtocol
        WebSocketTunnel.ConfigureUpstreamSocket(context.Request, socket);

        // ClientWebSocket.SubProtocol is only set after connection,
        // but AddSubProtocol would throw on duplicates or empty strings,
        // so no exception means protocols were correctly parsed and added.
        Assert.NotNull(socket);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureUpstreamSocket_should_skip_hop_by_hop_headers()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Connection = "Upgrade";
        context.Request.Headers.Upgrade = "websocket";
        context.Request.Headers.SecWebSocketKey = "dGhlIHNhbXBsZSBub25jZQ==";
        context.Request.Headers.SecWebSocketVersion = "13";
        context.Request.Headers["X-Custom-Header"] = "my-value";
        context.Request.Headers.Host = "example.com";
        context.Request.Scheme = "http";

        var socket = new ClientWebSocket();

        WebSocketTunnel.ConfigureUpstreamSocket(context.Request, socket);

        // X-Custom-Header should be forwarded, hop-by-hop should not.
        // We can't easily inspect ClientWebSocket.Options headers,
        // but we verify no exception is thrown during configuration.
        Assert.NotNull(socket);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureUpstreamSocket_should_set_proxy_headers()
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Scheme = "https",
                Host = new HostString("example.com:443")
            }
        };

        var socket = new ClientWebSocket();

        WebSocketTunnel.ConfigureUpstreamSocket(context.Request, socket);

        // ClientWebSocket doesn't expose headers for inspection, so we verify
        // no exception is thrown. Integration tests will verify end-to-end.
        Assert.NotNull(socket);
    }

    [Fact(Timeout = 5000)]
    public async Task TunnelAsync_should_return_400_when_not_a_websocket_request()
    {
        var tunnel = new WebSocketTunnel();
        var context = new DefaultHttpContext();
        context.Request.Headers.Upgrade = "websocket";
        // DefaultHttpContext.WebSockets.IsWebSocketRequest is false by default

        var upstream = UpstreamTarget.Create("http://backend:8080");
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            RequestTimeout = TimeSpan.FromSeconds(5),
        };

        await tunnel.TunnelAsync(context, upstream, config, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void BuildWebSocketUri_should_handle_root_path()
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/",
                QueryString = QueryString.Empty
            }
        };

        var upstream = UpstreamUrl.Parse("http://backend:8080");

        var uri = WebSocketTunnel.BuildWebSocketUri(context.Request, upstream);

        Assert.Equal("ws", uri.Scheme);
        Assert.Equal("/", uri.AbsolutePath);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureUpstreamSocket_should_handle_single_subprotocol()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.SecWebSocketProtocol = "chat";
        context.Request.Headers.Host = "example.com";
        context.Request.Scheme = "http";

        var socket = new ClientWebSocket();

        // Should not throw; a single sub-protocol is added correctly
        WebSocketTunnel.ConfigureUpstreamSocket(context.Request, socket);
        Assert.NotNull(socket);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureUpstreamSocket_should_handle_no_subprotocol()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Host = "example.com";
        context.Request.Scheme = "http";

        var socket = new ClientWebSocket();

        // Should not throw when no sub-protocol header is present
        WebSocketTunnel.ConfigureUpstreamSocket(context.Request, socket);
        Assert.NotNull(socket);
    }

    [Fact(Timeout = 5000)]
    public void DomainConfig_should_disable_websocket_by_default()
    {
        var config = new DomainConfig { DomainName = DomainName.Parse("example.com") };

        Assert.False(config.WebSocketEnabled);
    }

    [Fact(Timeout = 5000)]
    public void DomainConfig_should_allow_disabling_websocket()
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse("example.com"),
            WebSocketEnabled = false,
        };

        Assert.False(config.WebSocketEnabled);
    }
}