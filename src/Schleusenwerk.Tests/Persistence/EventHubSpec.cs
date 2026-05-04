using Akka.Actor;
using Akka.Streams;
using Akka.TestKit.Xunit;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class EventHubSpec : TestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact(Timeout = 5000)]
    public async Task EventHubActor_should_deliver_published_event_to_subscriber()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), "hub");
        var subscribed = await hub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance, Timeout,
            cancellationToken: TestContext.Current.CancellationToken);

        var received = new List<IClusterEvent>();
        var tcs = new TaskCompletionSource<IClusterEvent>();
        _ = subscribed.SourceRef.Source.RunForeach(evt =>
        {
            received.Add(evt);
            tcs.TrySetResult(evt);
        }, Sys.Materializer());

        var evt = new DomainAdded(new DomainConfig { DomainName = DomainName.Parse("hub-test.com") });
        hub.Tell(evt);

        var result = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.IsType<DomainAdded>(result);
        Assert.Equal("hub-test.com", ((DomainAdded)result).Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task EventHubActor_should_fan_out_to_multiple_subscribers()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), "hub-fanout");

        var tcs1 = new TaskCompletionSource<IClusterEvent>();
        var tcs2 = new TaskCompletionSource<IClusterEvent>();

        var sub1 = await hub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance, Timeout,
            cancellationToken: TestContext.Current.CancellationToken);
        var sub2 = await hub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance, Timeout,
            cancellationToken: TestContext.Current.CancellationToken);

        _ = sub1.SourceRef.Source.RunForeach(e => tcs1.TrySetResult(e), Sys.Materializer());
        _ = sub2.SourceRef.Source.RunForeach(e => tcs2.TrySetResult(e), Sys.Materializer());

        // Small delay to let streams materialise
        await Task.Delay(50, TestContext.Current.CancellationToken);

        hub.Tell(new DomainRemoved(DomainName.Parse("fanout.com")));

        var r1 = await tcs1.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        var r2 = await tcs2.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.IsType<DomainRemoved>(r1);
        Assert.IsType<DomainRemoved>(r2);
    }

    [Fact(Timeout = 5000)]
    public async Task EventHubActor_should_deliver_via_publisher_sink_ref()
    {
        var hub = Sys.ActorOf(Props.Create<EventHub>(), "hub-sinkref");
        var ready = await hub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance, Timeout,
            cancellationToken: TestContext.Current.CancellationToken);

        var tcs = new TaskCompletionSource<IClusterEvent>();
        var sub = await hub.Ask<EventHub.Subscribed>(EventHub.Subscribe.Instance, Timeout,
            cancellationToken: TestContext.Current.CancellationToken);
        _ = sub.SourceRef.Source.RunForeach(e => tcs.TrySetResult(e), Sys.Materializer());

        var queue = Akka.Streams.Dsl.Source
            .Queue<IClusterEvent>(10, OverflowStrategy.DropHead)
            .To(ready.SinkRef.Sink)
            .Run(Sys.Materializer());

        await Task.Delay(50, TestContext.Current.CancellationToken);
        await queue.OfferAsync(new DomainAdded(new DomainConfig { DomainName = DomainName.Parse("sinkref.com") }));

        var result = await tcs.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("sinkref.com", ((DomainAdded)result).Config.DomainName.Value);
    }
}