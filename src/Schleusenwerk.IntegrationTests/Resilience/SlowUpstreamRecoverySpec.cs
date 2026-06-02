using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Resilience;

[Collection("Integration")]
public sealed class SlowUpstreamRecoverySpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public SlowUpstreamRecoverySpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 120_000)]
    public async Task Circuit_should_open_on_latency_timeouts_and_recover()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("slow-recovery");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, timeoutSeconds: 3, ct: ct);

        try
        {
            using var toxiClient = _toxiproxy.CreateClient();

            // Add latency that exceeds the 3s request timeout → triggers 504s
            await toxiClient.AddLatencyAsync("echo", "slow", 10_000, ct: ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            // Fire requests to trigger circuit breaker (threshold = 3 consecutive failures)
            var failCount = 0;
            for (var i = 0; i < 5; i++)
            {
                var response = await proxyClient.GetAsync("/", ct);
                if (response.StatusCode is HttpStatusCode.GatewayTimeout or HttpStatusCode.BadGateway)
                {
                    failCount++;
                }
            }

            Assert.True(failCount >= 3, $"Expected at least 3 failures to trip circuit breaker, got {failCount}");

            // Remove the latency toxic — upstream is fast again
            await toxiClient.RemoveToxicAsync("echo", "slow", ct);

            // Wait for health check to detect recovery and close the circuit
            await TestHelper.WaitForHealthyAsync(_host.Client, domain, TimeSpan.FromSeconds(60), ct);

            // Verify traffic flows again — CB cooldown is 30s base, so allow up to 60s
            var recovered = false;
            for (var i = 0; i < 30; i++)
            {
                var response = await proxyClient.GetAsync("/", ct);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    recovered = true;
                    break;
                }

                await Task.Delay(2000, ct);
            }

            Assert.True(recovered, "Expected upstream to recover after toxic removal");
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
