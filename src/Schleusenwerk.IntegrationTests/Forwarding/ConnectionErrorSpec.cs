using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Forwarding;

[Collection("Integration")]
public sealed class ConnectionErrorSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public ConnectionErrorSpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_return_502_when_upstream_is_down()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("down");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, "http://localhost:19999", timeoutSeconds: 5, ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/", ct);
        Assert.True(response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout,
            $"Expected 502 or 504, got {(int)response.StatusCode}");
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_return_502_when_upstream_resets_connection()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("reset");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, timeoutSeconds: 5, ct: ct);

        try
        {
            using var client = _toxiproxy.CreateClient();
            await client.AddResetPeerAsync("echo", "reset", ct: ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
            var response = await proxyClient.GetAsync("/", ct);
            Assert.True(response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout,
                $"Expected 502 or 504, got {(int)response.StatusCode}");
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
