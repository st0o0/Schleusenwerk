using System.Net;
using System.Text;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Forwarding;

[Collection("Integration")]
public sealed class HttpForwardingSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly EchoServerFixture _echo;
    private readonly NginxFixture _nginx;

    public HttpForwardingSpec(SchleusenwerkTestHost host, EchoServerFixture echo, NginxFixture nginx)
    {
        _host = host;
        _echo = echo;
        _nginx = nginx;
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_forward_get_request_to_upstream()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("fwd-get");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _nginx.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("nginx", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_forward_post_with_body()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("fwd-post");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var payload = """{"test": "data"}""";
        var response = await client.PostAsync("/", new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("test", body);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_forward_put_and_delete()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("fwd-methods");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var putResponse = await client.PutAsync("/resource", new StringContent("updated", Encoding.UTF8, "text/plain"), ct);
        putResponse.EnsureSuccessStatusCode();
        var deleteResponse = await client.DeleteAsync("/resource", ct);
        deleteResponse.EnsureSuccessStatusCode();
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_preserve_response_status_codes()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("fwd-status");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _nginx.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/nonexistent-path-that-returns-404", ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_forward_query_string()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("fwd-qs");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/?foo=bar&baz=qux", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("foo", body);
        Assert.Contains("bar", body);
    }
}
