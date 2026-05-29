using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
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
            using var client = _toxiproxy.CreateClient();
            await client.AddLatencyAsync("echo", "slow", 30_000, ct: ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
            var response = await proxyClient.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
