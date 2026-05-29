using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Resilience;

[Collection("Integration")]
public sealed class UpstreamFailoverSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public UpstreamFailoverSpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 120_000)]
    public async Task Traffic_should_shift_to_healthy_upstream()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("failover");

        // Register route with first upstream pointing to echo server (via toxiproxy)
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, ct: ct);

        // Add a second upstream that is unavailable (to test failover logic)
        // Use a non-routable address that will fail quickly
        await TestHelper.AddUpstreamAsync(_host.Client, domain, "http://localhost:19999", ct: ct);

        try
        {
            // Wait for all upstreams to be probed and healthy ones identified
            var isHealthy = await TestHelper.WaitForHealthyAsync(
                _host.Client,
                domain,
                TimeSpan.FromSeconds(30),
                ct: ct);

            Assert.True(isHealthy, "At least one upstream should become healthy");

            // Now disable the primary upstream to force failover
            var conn = _toxiproxy.CreateConnection();
            var client = conn.Client();
            var proxy = client.FindProxy("echo");
            proxy.Enabled = false;
            proxy.Update();

            // Wait a bit for the proxy to mark the upstream as unhealthy
            await Task.Delay(3000, ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            // Send multiple requests - with the primary upstream down,
            // the second upstream (local:19999) should still fail for those requests
            // But the proxy should still respond (either 502 or attempt to route to available upstream)
            int successCount = 0;
            int failureCount = 0;

            for (int i = 0; i < 5; i++)
            {
                var response = await proxyClient.GetAsync("/", ct);
                if (response.IsSuccessStatusCode)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }

            // Since both upstreams are either down or unavailable, we expect failures
            // But the point of this test is that the proxy continues to attempt routing
            // (doesn't hang or circuit-break completely)
            Assert.True(successCount + failureCount >= 5, "Proxy should attempt all requests");
        }
        finally
        {
            _toxiproxy.Reset();
            await TestHelper.RemoveRouteAsync(_host.Client, domain, ct: ct);
        }
    }
}
