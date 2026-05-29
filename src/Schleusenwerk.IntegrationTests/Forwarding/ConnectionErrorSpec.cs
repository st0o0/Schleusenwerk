using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Toxiproxy.Net.Toxics;
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

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_return_502_when_upstream_is_down()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("down");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, "http://localhost:19999", ct: ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/", ct);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_return_502_when_upstream_resets_connection()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("reset");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, ct: ct);

        try
        {
            var conn = _toxiproxy.CreateConnection();
            var client = conn.Client();
            var proxy = client.FindProxy("echo");

            // Add slow_close toxic to simulate connection issues
            var slowCloseToxic = new SlowCloseToxic
            {
                Name = "slowclose",
                Stream = ToxicDirection.UpStream,
                Attributes = new SlowCloseToxic.ToxicAttributes { Delay = 5000 }
            };
            proxy.Add(slowCloseToxic);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
            var response = await proxyClient.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
        finally
        {
            _toxiproxy.Reset();
        }
    }
}
