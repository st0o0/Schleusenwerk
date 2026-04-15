using System.Net;
using Microsoft.AspNetCore.Http;
using Schleusenwerk.Domain.Routing;
using Schleusenwerk.Infrastructure.Forwarding;
using Xunit;

namespace Schleusenwerk.Tests.Infrastructure.Forwarding;

public sealed class RequestForwardingPipelineTests
{
    [Fact]
    public void BuildUpstreamUri_MapsPathAndQuery()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/data";
        context.Request.QueryString = new QueryString("?page=1");

        var upstream = UpstreamUrl.Parse("http://backend:8080");

        var result = RequestForwardingPipeline.BuildUpstreamUri(context.Request, upstream);

        Assert.Equal("http://backend:8080/api/data?page=1", result.ToString());
    }

    [Fact]
    public void BuildUpstreamUri_PreservesUpstreamSchemeAndHost()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/test";

        var upstream = UpstreamUrl.Parse("https://secure-backend:443");

        var result = RequestForwardingPipeline.BuildUpstreamUri(context.Request, upstream);

        Assert.Equal("https", result.Scheme);
        Assert.Equal("secure-backend", result.Host);
        Assert.Equal(443, result.Port);
    }

    [Fact]
    public void CreateRequestMessage_SetsMethodAndUri()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        var uri = new Uri("http://backend:8080/api");

        var msg = RequestForwardingPipeline.CreateRequestMessage(context.Request, uri);

        Assert.Equal(HttpMethod.Post, msg.Method);
        Assert.Equal(uri, msg.RequestUri);
        Assert.Equal(HttpVersion.Version11, msg.Version);
    }

    [Fact]
    public void SetProxyHeaders_SetsAllFourHeaders()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");

        using var msg = new HttpRequestMessage();

        RequestForwardingPipeline.SetProxyHeaders(context, msg);

        Assert.Equal("192.168.1.100", msg.Headers.GetValues(HeaderNames.XForwardedFor).Single());
        Assert.Equal("192.168.1.100", msg.Headers.GetValues(HeaderNames.XRealIp).Single());
        Assert.Equal("https", msg.Headers.GetValues(HeaderNames.XForwardedProto).Single());
        Assert.Equal("example.com", msg.Headers.GetValues(HeaderNames.XForwardedHost).Single());
    }

    [Fact]
    public void SetProxyHeaders_AppendsToExistingXForwardedFor()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("example.com");

        using var msg = new HttpRequestMessage();
        msg.Headers.TryAddWithoutValidation(HeaderNames.XForwardedFor, "203.0.113.50");

        RequestForwardingPipeline.SetProxyHeaders(context, msg);

        var value = msg.Headers.GetValues(HeaderNames.XForwardedFor).Single();
        Assert.Equal("203.0.113.50, 10.0.0.1", value);
    }

    [Fact]
    public void SetProxyHeaders_HandlesNullRemoteIp()
    {
        var context = new DefaultHttpContext();
        // RemoteIpAddress is null by default
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("example.com");

        using var msg = new HttpRequestMessage();

        RequestForwardingPipeline.SetProxyHeaders(context, msg);

        Assert.Equal("unknown", msg.Headers.GetValues(HeaderNames.XForwardedFor).Single());
        Assert.Equal("unknown", msg.Headers.GetValues(HeaderNames.XRealIp).Single());
    }

    [Fact]
    public void CopyRequestHeaders_CopiesStandardHeaders()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept"] = "application/json";
        context.Request.Headers["Authorization"] = "Bearer token123";
        context.Request.Headers["X-Custom"] = "custom-value";

        using var msg = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        RequestForwardingPipeline.CopyRequestHeaders(context.Request, msg);

        Assert.Equal("application/json", msg.Headers.GetValues("Accept").Single());
        Assert.Equal("Bearer token123", msg.Headers.GetValues("Authorization").Single());
        Assert.Equal("custom-value", msg.Headers.GetValues("X-Custom").Single());
    }

    [Fact]
    public void CopyRequestHeaders_SkipsHopByHopHeaders()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Connection"] = "keep-alive";
        context.Request.Headers["Keep-Alive"] = "timeout=5";
        context.Request.Headers["Transfer-Encoding"] = "chunked";
        context.Request.Headers["Upgrade"] = "websocket";
        context.Request.Headers["Accept"] = "text/html";

        using var msg = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        RequestForwardingPipeline.CopyRequestHeaders(context.Request, msg);

        Assert.False(msg.Headers.Contains("Connection"));
        Assert.False(msg.Headers.Contains("Keep-Alive"));
        Assert.False(msg.Headers.Contains("Transfer-Encoding"));
        Assert.False(msg.Headers.Contains("Upgrade"));
        Assert.True(msg.Headers.Contains("Accept"));
    }

    [Fact]
    public void CopyRequestHeaders_SkipsContentHeaders()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Content-Type"] = "application/json";
        context.Request.Headers["Content-Length"] = "42";
        context.Request.Headers["Accept"] = "text/html";

        using var msg = new HttpRequestMessage(HttpMethod.Post, "http://example.com");

        RequestForwardingPipeline.CopyRequestHeaders(context.Request, msg);

        // Content headers should not be on request headers — verify by enumerating
        var headerNames = msg.Headers.Select(h => h.Key).ToList();
        Assert.DoesNotContain("Content-Type", headerNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content-Length", headerNames, StringComparer.OrdinalIgnoreCase);
        Assert.True(msg.Headers.Contains("Accept"));
    }

    [Fact]
    public void RemoveHopByHopHeaders_RemovesAllHopByHopFromRequestHeaders()
    {
        using var msg = new HttpRequestMessage();
        msg.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        msg.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        msg.Headers.TryAddWithoutValidation("Proxy-Authenticate", "Basic");
        msg.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic abc");
        msg.Headers.TryAddWithoutValidation("TE", "trailers");
        msg.Headers.TryAddWithoutValidation("Trailers", "Expires");
        msg.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");
        msg.Headers.TryAddWithoutValidation("Upgrade", "h2c");
        msg.Headers.TryAddWithoutValidation("X-Custom", "keep-this");

        RequestForwardingPipeline.RemoveHopByHopHeaders(msg.Headers);

        Assert.False(msg.Headers.Contains("Connection"));
        Assert.False(msg.Headers.Contains("Keep-Alive"));
        Assert.False(msg.Headers.Contains("Proxy-Authenticate"));
        Assert.False(msg.Headers.Contains("Proxy-Authorization"));
        Assert.False(msg.Headers.Contains("TE"));
        Assert.False(msg.Headers.Contains("Trailers"));
        Assert.False(msg.Headers.Contains("Transfer-Encoding"));
        Assert.False(msg.Headers.Contains("Upgrade"));
        Assert.True(msg.Headers.Contains("X-Custom"));
    }

    [Theory]
    [InlineData("Connection", true)]
    [InlineData("connection", true)]
    [InlineData("CONNECTION", true)]
    [InlineData("Keep-Alive", true)]
    [InlineData("keep-alive", true)]
    [InlineData("Proxy-Authenticate", true)]
    [InlineData("Proxy-Authorization", true)]
    [InlineData("TE", true)]
    [InlineData("Trailers", true)]
    [InlineData("Transfer-Encoding", true)]
    [InlineData("Upgrade", true)]
    [InlineData("Accept", false)]
    [InlineData("Host", false)]
    [InlineData("X-Custom", false)]
    [InlineData("Content-Type", false)]
    public void IsHopByHopHeader_CorrectlyIdentifies(string header, bool expected)
    {
        Assert.Equal(expected, RequestForwardingPipeline.IsHopByHopHeader(header));
    }

    [Fact]
    public void SetProxyHeaders_WithIPv6Address()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::1");
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com:8443");

        using var msg = new HttpRequestMessage();

        RequestForwardingPipeline.SetProxyHeaders(context, msg);

        Assert.Equal("::1", msg.Headers.GetValues(HeaderNames.XForwardedFor).Single());
        Assert.Equal("::1", msg.Headers.GetValues(HeaderNames.XRealIp).Single());
        Assert.Equal("https", msg.Headers.GetValues(HeaderNames.XForwardedProto).Single());
        Assert.Equal("example.com:8443", msg.Headers.GetValues(HeaderNames.XForwardedHost).Single());
    }
}
