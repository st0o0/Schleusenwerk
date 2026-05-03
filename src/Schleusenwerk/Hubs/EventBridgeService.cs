using Akka.Actor;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.SignalR;
using Schleusenwerk.Api;
using Schleusenwerk.Persistence;

namespace Schleusenwerk.Hubs;

internal sealed class EventBridgeService : BackgroundService
{
    private readonly IReadOnlyActorRegistry _registry;
    private readonly IMaterializer _materializer;
    private readonly IHubContext<ProxyEventHub> _hub;
    private readonly ILogger<EventBridgeService> _logger;

    public EventBridgeService(
        IReadOnlyActorRegistry registry,
        IMaterializer materializer,
        IHubContext<ProxyEventHub> hub,
        ILogger<EventBridgeService> logger)
    {
        _registry = registry;
        _materializer = materializer;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StreamEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Event bridge disconnected, retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task StreamEventsAsync(CancellationToken ct)
    {
        var eventHub = _registry.Get<EventHub>();
        var subscribed = await eventHub.Ask<EventHub.Subscribed>(
            EventHub.Subscribe.Instance, TimeSpan.FromSeconds(5), ct);

        await subscribed.SourceRef.Source
            .Where(DomainModelMapper.CanMapToProxyEvent)
            .RunForeach(async evt =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var dto = DomainModelMapper.ToProxyEvent(evt);
                await _hub.Clients.All.SendAsync("OnProxyEvent", dto, ct);
            }, _materializer)
            .ConfigureAwait(false);
    }
}
