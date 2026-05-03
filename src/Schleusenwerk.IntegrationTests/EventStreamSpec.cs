using Grpc.Core;
using Schleusenwerk.Contracts;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class EventStreamSpec
{
    private readonly RouteService.RouteServiceClient _routes;
    private readonly EventService.EventServiceClient _events;

    public EventStreamSpec(SchleusenwerkFixture fixture)
    {
        _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
        _events = new EventService.EventServiceClient(fixture.GrpcChannel);
    }

    [Fact(Timeout = 30_000)]
    public async Task Subscribe_should_receive_route_updated_event()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var call = _events.Subscribe(
            new SubscribeRequest(),
            cancellationToken: cts.Token);

        var domain = $"evt-{Guid.NewGuid():N}.test";

        await Task.Delay(500, cts.Token);

        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        ProxyEvent? received = null;
        try
        {
            await foreach (var evt in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                if (evt.Type == EventType.RouteUpdated && evt.Domain == domain)
                {
                    received = evt;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.NotNull(received);
        Assert.Equal(EventType.RouteUpdated, received.Type);
        Assert.Equal(domain, received.Domain);
    }

    [Fact(Timeout = 30_000)]
    public async Task Subscribe_should_receive_route_removed_event()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var domain = $"evt-del-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = false,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        using var call = _events.Subscribe(new SubscribeRequest(), cancellationToken: cts.Token);

        await Task.Delay(500, cts.Token);

        await _routes.DeleteRouteAsync(new DeleteRouteRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);

        ProxyEvent? received = null;
        try
        {
            await foreach (var evt in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                if (evt.Type == EventType.RouteRemoved && evt.Domain == domain)
                {
                    received = evt;
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(received);
        Assert.Equal(EventType.RouteRemoved, received.Type);
    }

    [Fact(Timeout = 30_000)]
    public async Task Subscribe_with_domain_filter_should_only_receive_matching_events()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var targetDomain = $"evt-flt-{Guid.NewGuid():N}.test";
        var otherDomain = $"evt-oth-{Guid.NewGuid():N}.test";

        using var call = _events.Subscribe(
            new SubscribeRequest { Filter = $"domain:{targetDomain}" },
            cancellationToken: cts.Token);

        await Task.Delay(500, cts.Token);

        await _routes.AddRouteAsync(new AddRouteRequest { Domain = otherDomain, TimeoutSeconds = 30 }, cancellationToken: TestContext.Current.CancellationToken);
        await _routes.AddRouteAsync(new AddRouteRequest { Domain = targetDomain, TimeoutSeconds = 30 }, cancellationToken: TestContext.Current.CancellationToken);

        ProxyEvent? received = null;
        try
        {
            await foreach (var evt in call.ResponseStream.ReadAllAsync(cts.Token))
            {
                if (evt.Domain == targetDomain)
                {
                    received = evt;
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        Assert.NotNull(received);
        Assert.Equal(targetDomain, received.Domain);
    }
}
