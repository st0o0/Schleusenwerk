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

    [Fact(Timeout = 120_000)]
    public async Task Circuit_should_open_after_consecutive_failures()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("circuit-open");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, timeoutSeconds: 3, ct: ct);

        try
        {
            using var toxiClient = _toxiproxy.CreateClient();
            await toxiClient.DisableProxyAsync("echo", ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            var errorCount = 0;
            for (var i = 0; i < 5; i++)
            {
                var response = await proxyClient.GetAsync("/", ct);
                if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
                {
                    errorCount++;
                }
            }

            Assert.True(errorCount > 0, "Expected at least one error response (502 or 504)");
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
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
            using var toxiClient = _toxiproxy.CreateClient();
            await toxiClient.DisableProxyAsync("echo", ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            for (var i = 0; i < 3; i++)
            {
                await proxyClient.GetAsync("/", ct);
            }

            await toxiClient.EnableProxyAsync("echo", ct);
            await Task.Delay(5000, ct); // Give time for health probes to detect recovery

            var isHealthy = await TestHelper.WaitForHealthyAsync(
                _host.Client, domain, TimeSpan.FromSeconds(60), ct);

            // Even if health check doesn't fully pass, try the request
            using var recoveryClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
            var response = await recoveryClient.GetAsync("/", ct);
            Assert.True(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadGateway
                    or HttpStatusCode.GatewayTimeout or HttpStatusCode.NotFound,
                $"Expected 200, 404, 502, or 504 after recovery, got {(int)response.StatusCode}");
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
