using System.Net;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Routing;

[Collection("Integration")]
public sealed class UnknownDomainSpec
{
    private readonly SchleusenwerkTestHost _host;
    public UnknownDomainSpec(SchleusenwerkTestHost host) => _host = host;

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_return_404_for_unconfigured_domain()
    {
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, "unknown-domain.test");
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task Proxy_should_return_404_after_route_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("del-proxy");
        await TestHelper.RegisterRouteAsync(_host.Client, domain, "http://backend:8080", ct: ct);
        await TestHelper.RemoveRouteAsync(_host.Client, domain, ct);
        using var client = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await client.GetAsync("/", ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
