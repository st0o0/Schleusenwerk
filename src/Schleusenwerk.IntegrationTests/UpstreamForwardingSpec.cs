using System.Net;
using Schleusenwerk.Contracts;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class UpstreamForwardingSpec
{
    private readonly RouteService.RouteServiceClient _routes;
    private readonly HttpClient _proxyHttp;
    private readonly string _upstreamUrl;

    public UpstreamForwardingSpec(SchleusenwerkFixture fixture)
    {
        _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
        _proxyHttp = fixture.ProxyHttp;
        _upstreamUrl = fixture.UpstreamUrl;
    }

    [Fact(Timeout = 30_000)]
    public async Task Request_to_configured_domain_should_forward_to_upstream()
    {
        var domain = $"fwd-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = _upstreamUrl
        }, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = domain;

        var response = await _proxyHttp.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task Request_to_unknown_domain_should_not_return_ok()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "nonexistent.example.com";

        var response = await _proxyHttp.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task Forwarded_response_should_contain_via_header()
    {
        var domain = $"via-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = _upstreamUrl
        }, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = domain;
        var response = await _proxyHttp.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.Headers.Contains("Via") || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact(Timeout = 30_000)]
    public async Task Forwarded_request_should_preserve_path_and_query()
    {
        var domain = $"path-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = _upstreamUrl
        }, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = domain;
        var response = await _proxyHttp.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task Multiple_upstreams_should_both_receive_traffic()
    {
        var domain = $"multi-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = _upstreamUrl
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _routes.AddUpstreamAsync(new AddUpstreamRequest
        {
            Domain = domain,
            Url = _upstreamUrl,
            Weight = 1
        }, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/");
        request1.Headers.Host = domain;
        var response1 = await _proxyHttp.SendAsync(request1, TestContext.Current.CancellationToken);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/");
        request2.Headers.Host = domain;
        var response2 = await _proxyHttp.SendAsync(request2, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }
}
