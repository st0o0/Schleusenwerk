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
            using var toxiClient = _toxiproxy.CreateClient();
            await toxiClient.DisableProxyAsync("echo", ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            var badGatewayCount = 0;
            for (var i = 0; i < 5; i++)
            {
                var response = await proxyClient.GetAsync("/", ct);
                if (response.StatusCode == HttpStatusCode.BadGateway)
                {
                    badGatewayCount++;
                }
            }

            Assert.True(badGatewayCount > 0, "Expected at least one 502 Bad Gateway response");
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

            var isHealthy = await TestHelper.WaitForHealthyAsync(
                _host.Client, domain, TimeSpan.FromSeconds(30), ct);

            Assert.True(isHealthy, "Upstream should become healthy after recovery");

            var response = await proxyClient.GetAsync("/", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
