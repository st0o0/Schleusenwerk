using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Schleusenwerk.Domain.Routing;
using TurboHTTP;

namespace Schleusenwerk.Infrastructure.Forwarding;

/// <summary>
/// Converts incoming Kestrel requests to upstream HTTP requests via TurboHTTP,
/// streams responses back, and handles proxy headers and error conditions.
/// </summary>
internal sealed class RequestForwardingPipeline
{
    private readonly ITurboHttpClientFactory _clientFactory;

    public RequestForwardingPipeline(ITurboHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task ForwardAsync(HttpContext context, UpstreamTarget upstream, DomainConfig config)
    {
        var cancellationToken = context.RequestAborted;

        using var client = _clientFactory.CreateClient(string.Empty);
        client.Timeout = config.RequestTimeout;

        var upstreamUri = BuildUpstreamUri(context.Request, upstream.Url);
        using var requestMessage = CreateRequestMessage(context.Request, upstreamUri);

        CopyRequestHeaders(context.Request, requestMessage);
        SetProxyHeaders(context, requestMessage);
        RemoveHopByHopHeaders(requestMessage.Headers);

        if (HasRequestBody(context.Request))
        {
            requestMessage.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
                requestMessage.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
            if (context.Request.ContentLength.HasValue)
                requestMessage.Content.Headers.ContentLength = context.Request.ContentLength;
        }

        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await client.SendAsync(requestMessage, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return;
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }
        catch (SocketException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using (upstreamResponse)
        {
            await CopyResponseAsync(context.Response, upstreamResponse, cancellationToken);
        }
    }

    internal static Uri BuildUpstreamUri(HttpRequest request, UpstreamUrl upstream)
    {
        var builder = new UriBuilder(upstream.Value)
        {
            Path = request.Path.Value,
            Query = request.QueryString.Value,
        };
        return builder.Uri;
    }

    internal static HttpRequestMessage CreateRequestMessage(HttpRequest request, Uri upstreamUri)
    {
        return new HttpRequestMessage
        {
            Method = new HttpMethod(request.Method),
            RequestUri = upstreamUri,
            Version = HttpVersion.Version11,
        };
    }

    internal static void CopyRequestHeaders(HttpRequest source, HttpRequestMessage target)
    {
        foreach (var header in source.Headers)
        {
            if (IsHopByHopHeader(header.Key))
                continue;

            // Content headers go on Content, not on the request itself
            if (IsContentHeader(header.Key))
                continue;

            target.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    internal static void SetProxyHeaders(HttpContext context, HttpRequestMessage requestMessage)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var scheme = context.Request.Scheme;
        var host = context.Request.Host.ToString();

        // Append to existing X-Forwarded-For if present
        if (requestMessage.Headers.TryGetValues(HeaderNames.XForwardedFor, out var existingForwardedFor))
        {
            var existing = string.Join(", ", existingForwardedFor);
            requestMessage.Headers.Remove(HeaderNames.XForwardedFor);
            requestMessage.Headers.TryAddWithoutValidation(HeaderNames.XForwardedFor, $"{existing}, {remoteIp}");
        }
        else
        {
            requestMessage.Headers.TryAddWithoutValidation(HeaderNames.XForwardedFor, remoteIp);
        }

        requestMessage.Headers.TryAddWithoutValidation(HeaderNames.XRealIp, remoteIp);
        requestMessage.Headers.TryAddWithoutValidation(HeaderNames.XForwardedProto, scheme);
        requestMessage.Headers.TryAddWithoutValidation(HeaderNames.XForwardedHost, host);
    }

    internal static void RemoveHopByHopHeaders(System.Net.Http.Headers.HttpRequestHeaders headers)
    {
        foreach (var name in HopByHopHeaders)
        {
            headers.Remove(name);
        }
    }

    private static async Task CopyResponseAsync(
        HttpResponse target, HttpResponseMessage source, CancellationToken cancellationToken)
    {
        target.StatusCode = (int)source.StatusCode;

        // Copy response headers
        foreach (var header in source.Headers)
        {
            if (IsHopByHopHeader(header.Key))
                continue;
            target.Headers.Append(header.Key, header.Value.ToArray());
        }

        // Copy content headers
        foreach (var header in source.Content.Headers)
        {
            if (IsHopByHopHeader(header.Key))
                continue;
            target.Headers.Append(header.Key, header.Value.ToArray());
        }

        // Stream the response body without buffering
        await using var upstreamStream = await source.Content.ReadAsStreamAsync(cancellationToken);
        await upstreamStream.CopyToAsync(target.Body, cancellationToken);
    }

    private static bool HasRequestBody(HttpRequest request)
    {
        return request.ContentLength > 0
               || request.Headers.ContainsKey("Transfer-Encoding");
    }

    internal static bool IsHopByHopHeader(string headerName)
    {
        return HopByHopHeaders.Contains(headerName);
    }

    private static bool IsContentHeader(string headerName)
    {
        return ContentHeaders.Contains(headerName);
    }

    private static readonly HashSet<string> HopByHopHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Connection",
            "Keep-Alive",
            "Proxy-Authenticate",
            "Proxy-Authorization",
            "TE",
            "Trailers",
            "Transfer-Encoding",
            "Upgrade",
        };

    private static readonly HashSet<string> ContentHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Content-Type",
            "Content-Length",
            "Content-Disposition",
            "Content-Encoding",
            "Content-Language",
            "Content-Location",
            "Content-MD5",
            "Content-Range",
            "Expires",
            "Last-Modified",
            "Allow",
        };
}

internal static class HeaderNames
{
    public const string XForwardedFor = "X-Forwarded-For";
    public const string XRealIp = "X-Real-IP";
    public const string XForwardedProto = "X-Forwarded-Proto";
    public const string XForwardedHost = "X-Forwarded-Host";
}
