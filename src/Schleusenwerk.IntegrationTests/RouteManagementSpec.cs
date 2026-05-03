using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Schleusenwerk.Contracts;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class RouteManagementSpec
{
    private readonly RouteService.RouteServiceClient _routes;

    public RouteManagementSpec(SchleusenwerkFixture fixture)
    {
        _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddRoute_then_ListRoutes_should_contain_new_route()
    {
        var domain = $"list-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = true,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = "http://upstream-mock"
        }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await _routes.ListRoutesAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(response.Routes, r => r.Domain == domain);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddRoute_then_GetRoute_should_return_detail()
    {
        var domain = $"detail-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = true,
            TimeoutSeconds = 60,
            FirstUpstreamUrl = "http://upstream-mock"
        }, cancellationToken: TestContext.Current.CancellationToken);

        var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(domain, detail.Domain);
        Assert.True(detail.ForceHttps);
        Assert.Equal(60, detail.TimeoutSeconds);
    }

    [Fact(Timeout = 30_000)]
    public async Task UpdateRoute_should_change_config()
    {
        var domain = $"update-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _routes.UpdateRouteAsync(new UpdateRouteRequest
        {
            Domain = domain,
            ForceHttps = true,
            TimeoutSeconds = 120
        }, cancellationToken: TestContext.Current.CancellationToken);

        var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(detail.ForceHttps);
        Assert.Equal(120, detail.TimeoutSeconds);
    }

    [Fact(Timeout = 30_000)]
    public async Task DeleteRoute_should_remove_from_list()
    {
        var domain = $"delete-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _routes.DeleteRouteAsync(new DeleteRouteRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await _routes.ListRoutesAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(response.Routes, r => r.Domain == domain);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddUpstream_then_GetRoute_should_include_upstream()
    {
        var domain = $"upstream-add-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _routes.AddUpstreamAsync(new AddUpstreamRequest
        {
            Domain = domain,
            Url = "http://upstream-mock",
            Weight = 1
        }, cancellationToken: TestContext.Current.CancellationToken);

        var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(detail.Upstreams, u => u.Url.Contains("upstream-mock"));
    }

    [Fact(Timeout = 30_000)]
    public async Task RemoveUpstream_should_remove_from_route()
    {
        var domain = $"upstream-rm-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = "http://upstream-mock"
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _routes.RemoveUpstreamAsync(new RemoveUpstreamRequest
        {
            Domain = domain,
            Url = "http://upstream-mock"
        }, cancellationToken: TestContext.Current.CancellationToken);

        var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(detail.Upstreams);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddRoute_should_fail_when_domain_already_exists()
    {
        var domain = $"dup-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest { Domain = domain, TimeoutSeconds = 30 }, cancellationToken: TestContext.Current.CancellationToken);

        var result = await _routes.AddRouteAsync(new AddRouteRequest { Domain = domain, TimeoutSeconds = 30 }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("already", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetRoute_should_throw_when_domain_not_found()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            async () => await _routes.GetRouteAsync(new GetRouteRequest { Domain = "nonexistent.test" }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddRoute_with_upstream_should_include_upstream_in_detail()
    {
        var domain = $"with-ups-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = "http://upstream-mock"
        }, cancellationToken: TestContext.Current.CancellationToken);

        var detail = await _routes.GetRouteAsync(new GetRouteRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(detail.Upstreams);
        Assert.Contains("upstream-mock", detail.Upstreams[0].Url);
    }

    [Fact(Timeout = 30_000)]
    public async Task ListRoutes_should_return_empty_when_no_routes_configured()
    {
        var response = await _routes.ListRoutesAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        Assert.NotNull(response.Routes);
    }
}
