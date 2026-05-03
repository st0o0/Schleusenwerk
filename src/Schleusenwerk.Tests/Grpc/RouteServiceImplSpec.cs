using Google.Protobuf.WellKnownTypes;
using Schleusenwerk.Contracts;
using Schleusenwerk.Grpc;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Grpc;

public sealed class RouteServiceImplSpec
{
    private readonly FakeConfigurationService _config = new();
    private RouteServiceImpl CreateSut() => new(_config);

    [Fact(Timeout = 5000)]
    public async Task ListRoutes_should_return_all_configured_domains()
    {
        _config.Seed(
            new DomainConfig { DomainName = DomainName.Parse("api.example.com"), ForceHttps = true },
            UpstreamTarget.Create("http://backend:8080"));

        var response = await CreateSut().ListRoutes(new Empty(), FakeServerCallContext.Instance);

        Assert.Single(response.Routes);
        Assert.Equal("api.example.com", response.Routes[0].Domain);
        Assert.True(response.Routes[0].ForceHttps);
    }

    [Fact(Timeout = 5000)]
    public async Task AddRoute_should_return_success_result()
    {
        var request = new AddRouteRequest
        {
            Domain = "new.example.com",
            ForceHttps = true,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = "http://backend:8080"
        };

        var result = await CreateSut().AddRoute(request, FakeServerCallContext.Instance);

        Assert.True(result.Success);
    }

    [Fact(Timeout = 5000)]
    public async Task AddRoute_should_return_failure_when_service_fails()
    {
        _config.MakeNextCommandFail("Domain already exists");
        var request = new AddRouteRequest
        {
            Domain = "existing.example.com",
            ForceHttps = false,
            TimeoutSeconds = 30,
            FirstUpstreamUrl = "http://backend:8080"
        };

        var result = await CreateSut().AddRoute(request, FakeServerCallContext.Instance);

        Assert.False(result.Success);
        Assert.Equal("Domain already exists", result.ErrorMessage);
    }

    [Fact(Timeout = 5000)]
    public async Task DeleteRoute_should_return_success_result()
    {
        var request = new DeleteRouteRequest { Domain = "example.com" };

        var result = await CreateSut().DeleteRoute(request, FakeServerCallContext.Instance);

        Assert.True(result.Success);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstream_should_return_success_result()
    {
        var request = new AddUpstreamRequest
        {
            Domain = "example.com",
            Url = "http://backend2:9090",
            Weight = 1
        };

        var result = await CreateSut().AddUpstream(request, FakeServerCallContext.Instance);

        Assert.True(result.Success);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveUpstream_should_return_success_result()
    {
        var request = new RemoveUpstreamRequest
        {
            Domain = "example.com",
            Url = "http://backend:8080"
        };

        var result = await CreateSut().RemoveUpstream(request, FakeServerCallContext.Instance);

        Assert.True(result.Success);
    }
}
