using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Routing;

[Collection("Integration")]
public sealed class MultiUpstreamSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly EchoServerFixture _echo;
    private readonly NginxFixture _nginx;

    public MultiUpstreamSpec(SchleusenwerkTestHost host, EchoServerFixture echo, NginxFixture nginx)
    {
        _host = host;
        _echo = echo;
        _nginx = nginx;
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_distribute_requests_across_upstreams()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("multi");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _echo.BaseUrl, ct: ct);
        await TestHelper.AddUpstreamAsync(_host.Client, domain, _nginx.BaseUrl, ct);
        await TestHelper.WaitForHealthyAsync(_host.Client, domain, TimeSpan.FromSeconds(30), ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var responses = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/", ct);
            response.EnsureSuccessStatusCode();
            responses.Add(await response.Content.ReadAsStringAsync(ct));
            await Task.Delay(100, ct);
        }
        var echoCount = responses.Count(r => r.Contains("request", StringComparison.OrdinalIgnoreCase));
        var nginxCount = responses.Count(r => r.Contains("nginx", StringComparison.OrdinalIgnoreCase));
        Assert.True(echoCount > 0, "Echo server should have received some requests");
        Assert.True(nginxCount > 0, "Nginx should have received some requests");
    }
}
