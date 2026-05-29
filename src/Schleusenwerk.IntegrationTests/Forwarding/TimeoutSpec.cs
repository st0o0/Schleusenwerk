using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Toxiproxy.Net.Toxics;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Forwarding;

[Collection("Integration")]
public sealed class TimeoutSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public TimeoutSpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_return_504_when_upstream_times_out()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("timeout");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, timeoutSeconds: 5, ct: ct);

        try
        {
            var conn = _toxiproxy.CreateConnection();
            var client = conn.Client();
            var proxy = client.FindProxy("echo");

            // Add latency toxic that exceeds proxy timeout (30 seconds of latency)
            var latencyToxic = new LatencyToxic
            {
                Name = "slow",
                Stream = ToxicDirection.UpStream,
                Attributes = new LatencyToxic.ToxicAttributes { Latency = 30000 }
            };
            proxy.Add(latencyToxic);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
            var response = await proxyClient.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        }
        finally
        {
            _toxiproxy.Reset();
        }
    }
}
