using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.SignalR;
using Schleusenwerk.Contracts;
using Schleusenwerk.UI.Hubs;

namespace Schleusenwerk.UI.Services;

internal sealed class EventStreamBackgroundService : BackgroundService
{
    private readonly GrpcChannel _channel;
    private readonly IHubContext<ProxyEventHub> _hub;
    private readonly ILogger<EventStreamBackgroundService> _logger;

    public EventStreamBackgroundService(
        GrpcChannel channel,
        IHubContext<ProxyEventHub> hub,
        ILogger<EventStreamBackgroundService> logger)
    {
        _channel = channel;
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
                _logger.LogWarning(ex, "Event stream disconnected, retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    private async Task StreamEventsAsync(CancellationToken ct)
    {
        var client = new EventService.EventServiceClient(_channel);
        using var call = client.Subscribe(new SubscribeRequest(), cancellationToken: ct);

        await foreach (var evt in call.ResponseStream.ReadAllAsync(ct))
        {
            await ProxyEventHub.BroadcastAsync(_hub, evt);
        }
    }
}
