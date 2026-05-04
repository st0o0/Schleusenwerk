using System.Net.WebSockets;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Forwarding;

/// <summary>
/// Establishes a bidirectional WebSocket tunnel between the client and an upstream server.
/// Detects upgrade requests and handles the full lifecycle including timeout and close.
/// </summary>
internal sealed class WebSocketTunnel
{
    private const int BufferSize = 8192;

    private static readonly HashSet<string> WebSocketForwardHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Sec-WebSocket-Protocol",
            "Sec-WebSocket-Extensions",
        };

    private static readonly HashSet<string> SkipHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Connection",
            "Upgrade",
            "Sec-WebSocket-Key",
            "Sec-WebSocket-Version",
            "Sec-WebSocket-Accept",
            "Host",
        };

    /// <summary>
    /// Returns true when the request is a WebSocket upgrade request.
    /// </summary>
    public static bool IsWebSocketUpgrade(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Upgrade", out var upgradeValue))
        {
            return false;
        }

        return string.Equals(upgradeValue.ToString(), "websocket", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tunnels a WebSocket connection between the client and the upstream target.
    /// </summary>
    public async Task TunnelAsync(
        HttpContext context,
        UpstreamTarget upstream,
        DomainConfig config,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var upstreamUri = BuildWebSocketUri(context.Request, upstream.Url);
        using var upstreamSocket = new ClientWebSocket();

        ConfigureUpstreamSocket(context.Request, upstreamSocket);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(config.RequestTimeout);

        try
        {
            await upstreamSocket.ConnectAsync(upstreamUri, connectCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return;
        }
        catch (WebSocketException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using var clientSocket = await context.WebSockets.AcceptWebSocketAsync(
            upstreamSocket.SubProtocol);

        await RunBidirectionalTunnel(clientSocket, upstreamSocket, cancellationToken);
    }

    internal static Uri BuildWebSocketUri(HttpRequest request, UpstreamUrl upstream)
    {
        var scheme = upstream.Scheme switch
        {
            "https" => "wss",
            _ => "ws",
        };

        var builder = new UriBuilder(upstream.Value)
        {
            Scheme = scheme,
            Path = request.Path.Value,
            Query = request.QueryString.Value,
        };
        return builder.Uri;
    }

    internal static void ConfigureUpstreamSocket(HttpRequest request, ClientWebSocket upstreamSocket)
    {
        foreach (var header in request.Headers)
        {
            if (SkipHeaders.Contains(header.Key))
            {
                continue;
            }

            if (WebSocketForwardHeaders.Contains(header.Key))
            {
                if (string.Equals(header.Key, "Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var protocol in header.Value.ToString().Split(',', StringSplitOptions.TrimEntries))
                    {
                        upstreamSocket.Options.AddSubProtocol(protocol);
                    }
                }
                continue;
            }

            if (RequestForwardingPipeline.IsHopByHopHeader(header.Key))
            {
                continue;
            }

            upstreamSocket.Options.SetRequestHeader(header.Key, header.Value.ToString());
        }

        // Set proxy headers
        upstreamSocket.Options.SetRequestHeader(HeaderNames.XForwardedProto, request.Scheme);
        upstreamSocket.Options.SetRequestHeader(HeaderNames.XForwardedHost, request.Host.ToString());
    }

    private static async Task RunBidirectionalTunnel(
        WebSocket clientSocket,
        WebSocket upstreamSocket,
        CancellationToken cancellationToken)
    {
        var clientToUpstream = RelayAsync(clientSocket, upstreamSocket, cancellationToken);
        var upstreamToClient = RelayAsync(upstreamSocket, clientSocket, cancellationToken);

        var completed = await Task.WhenAny(clientToUpstream, upstreamToClient);

        // When one direction finishes, initiate close on the other
        try
        {
            if (completed == clientToUpstream)
            {
                await CloseIfOpen(upstreamSocket, cancellationToken);
            }
            else
            {
                await CloseIfOpen(clientSocket, cancellationToken);
            }
        }
        catch (WebSocketException)
        {
            // Peer may have already disconnected
        }
        catch (OperationCanceledException)
        {
            // Connection was cancelled
        }

        // Await the other task to observe exceptions
        try
        {
            await Task.WhenAll(clientToUpstream, upstreamToClient);
        }
        catch (WebSocketException)
        {
            // Expected when one side closes abruptly
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
    }

    private static async Task RelayAsync(
        WebSocket source,
        WebSocket destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];

        while (source.State == WebSocketState.Open)
        {
            var result = await source.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseIfOpen(destination, cancellationToken);
                return;
            }

            if (destination.State == WebSocketState.Open)
            {
                await destination.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType,
                    result.EndOfMessage,
                    cancellationToken);
            }
        }
    }

    private static async Task CloseIfOpen(WebSocket socket, CancellationToken cancellationToken)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            closeCts.CancelAfter(TimeSpan.FromSeconds(5));

            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "Tunnel closing",
                closeCts.Token);
        }
    }
}
