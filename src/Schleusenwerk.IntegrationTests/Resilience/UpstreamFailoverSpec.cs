using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Resilience;

[Collection("Integration")]
public sealed class UpstreamFailoverSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly EchoServerFixture _echo;
    private readonly ToxiproxyFixture _toxiproxy;

    public UpstreamFailoverSpec(SchleusenwerkTestHost host, EchoServerFixture echo, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _echo = echo;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 120_000)]
    public async Task Traffic_should_shift_to_healthy_upstream()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("failover");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        await TestHelper.AddUpstreamAsync(_host.Client, domain, _toxiproxy.ProxyUrl, ct);

        await TestHelper.WaitForHealthyAsync(_host.Client, domain, TimeSpan.FromSeconds(60), ct);

        try
        {
            using var toxiClient = _toxiproxy.CreateClient();
            await toxiClient.DisableProxyAsync("echo", ct);

            await Task.Delay(10_000, ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
            var successCount = 0;
            for (var i = 0; i < 5; i++)
            {
                var response = await proxyClient.GetAsync("/", ct);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    successCount++;
                }

                await Task.Delay(500, ct);
            }

            Assert.True(successCount >= 3,
                $"Expected most requests to succeed via healthy upstream, got {successCount}/5");
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
