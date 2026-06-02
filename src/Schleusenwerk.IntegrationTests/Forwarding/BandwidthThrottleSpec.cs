using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Forwarding;

[Collection("Integration")]
public sealed class BandwidthThrottleSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public BandwidthThrottleSpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_deliver_complete_response_under_bandwidth_throttle()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("bw-throttle");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, timeoutSeconds: 30, ct: ct);

        try
        {
            using var toxiClient = _toxiproxy.CreateClient();
            await toxiClient.AddBandwidthAsync("echo", "slow-bw", 1024, ct: ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
            var response = await proxyClient.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(ct);
            Assert.NotEmpty(body);
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
