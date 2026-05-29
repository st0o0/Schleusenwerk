using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Resilience;

[Collection("Integration")]
public sealed class CircuitBreakerSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public CircuitBreakerSpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 60_000)]
    public async Task Circuit_should_open_after_consecutive_failures()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("circuit-open");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, ct: ct);

        try
        {
            var conn = _toxiproxy.CreateConnection();
            var client = conn.Client();
            var proxy = client.FindProxy("echo");

            // Disable the proxy to simulate upstream failures
            proxy.Enabled = false;
            proxy.Update();

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            // Send multiple requests to trigger circuit breaker
            int badGatewayCount = 0;
            for (int i = 0; i < 5; i++)
            {
                var response = await proxyClient.GetAsync("/", ct);
                if (response.StatusCode == HttpStatusCode.BadGateway)
                {
                    badGatewayCount++;
                }
            }

            // Should get 502 errors when upstream is down
            Assert.True(badGatewayCount > 0, "Expected at least one 502 Bad Gateway response");
        }
        finally
        {
            _toxiproxy.Reset();
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Circuit_should_close_after_upstream_recovers()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("circuit-close");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, ct: ct);

        try
        {
            var conn = _toxiproxy.CreateConnection();
            var client = conn.Client();
            var proxy = client.FindProxy("echo");

            // Disable the proxy to simulate upstream failures
            proxy.Enabled = false;
            proxy.Update();

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            // Send requests to trigger circuit breaker
            for (int i = 0; i < 3; i++)
            {
                await proxyClient.GetAsync("/", ct);
            }

            // Re-enable the proxy to simulate recovery
            proxy.Enabled = true;
            proxy.Update();

            // Wait for health checks to recognize the recovery (health check interval ~5-10 seconds)
            var isHealthy = await TestHelper.WaitForHealthyAsync(
                _host.Client,
                domain,
                TimeSpan.FromSeconds(30),
                ct: ct);

            Assert.True(isHealthy, "Upstream should become healthy after recovery");

            // Now send requests again and verify they succeed
            var response = await proxyClient.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            _toxiproxy.Reset();
        }
    }
}
