using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Cluster Singleton actor that owns a single MergeHub → BroadcastHub pipeline.
/// Publishers obtain an ISinkRef via GetPublisher and feed events through a local Source.Queue.
/// Subscribers obtain an ISourceRef via Subscribe and receive events via RunForeach.
/// Low-frequency publishers may Tell IClusterEvent directly.
/// </summary>
public sealed class EventHub : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer = Context.Materializer();
    private Sink<IClusterEvent, NotUsed> _mergeHubSink = null!;
    private Source<IClusterEvent, NotUsed> _broadcastSource = null!;
    private ISourceQueueWithComplete<IClusterEvent> _internalQueue = null!;

    public EventHub()
    {
        Receive<IClusterEvent>(Handle);
        Receive<GetPublisher>(Handle);
        Receive<FilteredSubscribe>(HandleFiltered);
        Receive<Subscribe>(Handle);
        Receive<IQueueOfferResult>(r =>
        {
            if (r is QueueOfferResult.Dropped)
            {
                _log.Warning("EventHub internal queue dropped an event");
            }
        });
        Receive<Status.Failure>(f =>
            _log.Error(f.Cause, "EventHub internal queue failure"));
    }

    protected override void PreStart()
    {
        (_mergeHubSink, _broadcastSource) = MergeHub.Source<IClusterEvent>(perProducerBufferSize: 16)
            .ToMaterialized(BroadcastHub.Sink<IClusterEvent>(bufferSize: 256), Keep.Both)
            .Run(_materializer);

        _internalQueue = Source.Queue<IClusterEvent>(100, OverflowStrategy.DropHead)
            .To(_mergeHubSink)
            .Run(_materializer);

        _log.Info("EventHubActor started");
    }

    private void Handle(IClusterEvent evt)
    {
        _internalQueue.OfferAsync(evt).PipeTo(Self);
    }

    private void Handle(GetPublisher _)
    {
        StreamRefs.SinkRef<IClusterEvent>()
            .To(_mergeHubSink)
            .Run(_materializer)
            .PipeTo(Sender, Self, sink => new PublisherReady(sink));
    }

    private void Handle(Subscribe _)
    {
        _broadcastSource
            .ToMaterialized(StreamRefs.SourceRef<IClusterEvent>(), Keep.Right)
            .Run(_materializer)
            .PipeTo(Sender, Self, sourceRef => new Subscribed(sourceRef));
    }

    private void HandleFiltered(FilteredSubscribe msg)
    {
        _broadcastSource
            .Where(evt => msg.FilterType.IsInstanceOfType(evt))
            .ToMaterialized(StreamRefs.SourceRef<IClusterEvent>(), Keep.Right)
            .Run(_materializer)
            .PipeTo(Sender, Self, sourceRef => new Subscribed(sourceRef));
    }

    public sealed record GetPublisher
    {
        public static readonly GetPublisher Instance = new();
    }

    public sealed record Subscribe
    {
        public static readonly Subscribe Instance = new();
    }

    public abstract record FilteredSubscribe(Type FilterType);

    public sealed record Subscribe<T> : FilteredSubscribe where T : IClusterEvent
    {
        public static readonly Subscribe<T> Instance = new();
        public Subscribe() : base(typeof(T)) { }
    }

    public sealed record PublisherReady(ISinkRef<IClusterEvent> SinkRef);

    public sealed record Subscribed(ISourceRef<IClusterEvent> SourceRef);
}