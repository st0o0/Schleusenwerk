using Google.Protobuf.WellKnownTypes;
using Schleusenwerk.Contracts;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class HealthSpec
{
    private readonly HealthService.HealthServiceClient _health;
    private readonly RouteService.RouteServiceClient _routes;

    public HealthSpec(SchleusenwerkFixture fixture)
    {
        _health = new HealthService.HealthServiceClient(fixture.GrpcChannel);
        _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetHealth_should_return_route_counts()
    {
        var response = await _health.GetHealthAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.RouteCount >= 0);
        Assert.True(response.HealthyCount >= 0);
        Assert.True(response.UnhealthyCount >= 0);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetUpstreamHealth_should_return_entries_for_domain_with_upstreams()
    {
        var domain = $"health-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = "http://upstream-mock"
        }, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var response = await _health.GetUpstreamHealthAsync(new GetUpstreamHealthRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(domain, response.Domain);
        Assert.NotEmpty(response.Upstreams);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetUpstreamHealth_should_return_empty_for_domain_without_upstreams()
    {
        var domain = $"health-no-ups-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var response = await _health.GetUpstreamHealthAsync(new GetUpstreamHealthRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(domain, response.Domain);
        Assert.Empty(response.Upstreams);
    }
}
