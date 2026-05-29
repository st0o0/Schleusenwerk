using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Forwarding;

[Collection("Integration")]
public sealed class ProxyHeaderSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly EchoServerFixture _echo;

    public ProxyHeaderSpec(SchleusenwerkTestHost host, EchoServerFixture echo)
    {
        _host = host;
        _echo = echo;
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_set_x_forwarded_for()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("hdr-xff");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("x-forwarded-for", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_set_x_forwarded_host()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("hdr-xfh");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("x-forwarded-host", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_set_x_forwarded_proto()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("hdr-xfp");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("x-forwarded-proto", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_strip_hop_by_hop_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("hdr-hop");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
        var response = await client.GetAsync("/", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        var echoData = JsonSerializer.Deserialize<JsonElement>(body);
        if (echoData.TryGetProperty("request", out var request) && request.TryGetProperty("headers", out var headers))
        {
            Assert.False(headers.TryGetProperty("connection", out _), "Hop-by-hop header 'Connection' should be stripped");
        }
    }
}
