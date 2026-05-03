using Akka.Actor;
using Akka.Hosting;
using Akka.Streams;
using Akka.Streams.Dsl;
using Grpc.Core;
using Schleusenwerk.Contracts;
using Schleusenwerk.Persistence;

namespace Schleusenwerk.Grpc;

internal sealed class EventServiceImpl : EventService.EventServiceBase
{
    private readonly IReadOnlyActorRegistry _registry;
    private readonly IMaterializer _materializer;

    public EventServiceImpl(IReadOnlyActorRegistry registry, IMaterializer materializer)
    {
        _registry = registry;
        _materializer = materializer;
    }

    public override async Task Subscribe(
        SubscribeRequest request, IServerStreamWriter<ProxyEvent> responseStream, ServerCallContext context)
    {
        var eventHub = _registry.Get<EventHub>();
        var timeout = TimeSpan.FromSeconds(5);

        try
        {
            var subscribed = await eventHub.Ask<EventHub.Subscribed>(
                EventHub.Subscribe.Instance, timeout, context.CancellationToken);

            await subscribed.SourceRef.Source
                .Where(evt => ProtoMapper.CanMapToProxyEvent(evt))
                .RunForeach(async evt =>
                {
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var proxyEvent = ProtoMapper.ToProxyEvent(evt);

                    if (MatchesFilter(proxyEvent, request.Filter))
                    {
                        await responseStream.WriteAsync(proxyEvent);
                    }
                }, _materializer)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            throw new RpcException(
                new global::Grpc.Core.Status(StatusCode.Internal, $"Event streaming failed: {ex.Message}", ex));
        }
    }

    private static bool MatchesFilter(ProxyEvent evt, string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        if (filter == "*")
        {
            return true;
        }

        if (filter.StartsWith("domain:"))
        {
            var domain = filter["domain:".Length..];
            return evt.Domain == domain;
        }

        if (filter.StartsWith("type:"))
        {
            var typeName = filter["type:".Length..];
            return evt.Type.ToString() == typeName;
        }

        return true;
    }
}
