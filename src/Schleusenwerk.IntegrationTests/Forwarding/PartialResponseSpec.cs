using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Forwarding;

[Collection("Integration")]
public sealed class PartialResponseSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public PartialResponseSpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_handle_truncated_upstream_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("partial");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, timeoutSeconds: 10, ct: ct);

        try
        {
            using var toxiClient = _toxiproxy.CreateClient();
            // Cut the downstream after 10 bytes — upstream response gets truncated
            await toxiClient.AddLimitDataAsync("echo", "cut-response", 10, ct: ct);

            using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);

            try
            {
                var response = await proxyClient.GetAsync("/", ct);
                // Either 502 (proxy detects broken stream) or partial read that throws on body
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Response headers came through but body may be truncated — reading should fail or be short
                    var body = await response.Content.ReadAsStringAsync(ct);
                    Assert.True(body.Length <= 50, $"Expected truncated body, got {body.Length} bytes");
                }
                else
                {
                    Assert.True(
                        response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout,
                        $"Expected 502 or 504, got {(int)response.StatusCode}");
                }
            }
            catch (HttpRequestException)
            {
                // Connection cut mid-stream — this is valid behavior
            }
        }
        finally
        {
            await _toxiproxy.ResetAsync(ct);
        }
    }
}
