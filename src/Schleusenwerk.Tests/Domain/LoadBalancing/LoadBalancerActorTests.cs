namespace Schleusenwerk.Tests.Domain.LoadBalancing;

using Akka.Actor;
using Schleusenwerk.Domain.LoadBalancing;
using Schleusenwerk.Domain.Routing;
using Xunit;

public sealed class LoadBalancerActorTests : IAsyncDisposable
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private readonly ActorSystem _system = ActorSystem.Create("load-balancer-tests");

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact]
    public async Task SingleUpstream_AlwaysReturnsSameTarget()
    {
        var upstream = UpstreamTarget.Create("http://backend1:8080");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([upstream]));

        for (var i = 0; i < 5; i++)
        {
            var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
            Assert.Equal(upstream.Url, result.Target.Url);
        }
    }

    [Fact]
    public async Task TwoUpstreams_DistributesEvenly()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var b = UpstreamTarget.Create("http://b:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a, b]));

        var counts = new Dictionary<string, int>();
        const int requests = 100;

        for (var i = 0; i < requests; i++)
        {
            var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
            var host = result.Target.Url.Host;
            counts[host] = counts.GetValueOrDefault(host) + 1;
        }

        Assert.Equal(2, counts.Count);
        Assert.Equal(50, counts["a"]);
        Assert.Equal(50, counts["b"]);
    }

    [Fact]
    public async Task ThreeUpstreams_RoundRobinCycles()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var b = UpstreamTarget.Create("http://b:80");
        var c = UpstreamTarget.Create("http://c:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a, b, c]));

        var sequence = new List<string>();
        for (var i = 0; i < 9; i++)
        {
            var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
            sequence.Add(result.Target.Url.Host);
        }

        // Each upstream should appear exactly 3 times
        Assert.Equal(3, sequence.Count(h => h == "a"));
        Assert.Equal(3, sequence.Count(h => h == "b"));
        Assert.Equal(3, sequence.Count(h => h == "c"));
    }

    [Fact]
    public async Task WeightedUpstreams_ProportionalDistribution()
    {
        // Weight 2 should get twice as many requests as weight 1
        var heavy = UpstreamTarget.Create("http://heavy:80", weight: 2);
        var light = UpstreamTarget.Create("http://light:80", weight: 1);
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([heavy, light]));

        var counts = new Dictionary<string, int>();
        const int requests = 90; // divisible by 3 (2+1 routees)

        for (var i = 0; i < requests; i++)
        {
            var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
            var host = result.Target.Url.Host;
            counts[host] = counts.GetValueOrDefault(host) + 1;
        }

        Assert.Equal(60, counts["heavy"]); // 2/3 of 90
        Assert.Equal(30, counts["light"]); // 1/3 of 90
    }

    [Fact]
    public async Task MarkUnhealthy_SkipsUnhealthyUpstream()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var b = UpstreamTarget.Create("http://b:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a, b]));

        // Mark 'a' unhealthy
        actor.Tell(new MarkUpstreamUnhealthy(a.Url));

        // Allow rebuild to complete
        await Task.Delay(100);

        for (var i = 0; i < 5; i++)
        {
            var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
            Assert.Equal("b", result.Target.Url.Host);
        }
    }

    [Fact]
    public async Task MarkHealthy_RestoresUpstream()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var b = UpstreamTarget.Create("http://b:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a, b]));

        // Mark 'a' unhealthy, then healthy again
        actor.Tell(new MarkUpstreamUnhealthy(a.Url));
        await Task.Delay(100);
        actor.Tell(new MarkUpstreamHealthy(a.Url));
        await Task.Delay(100);

        var hosts = new HashSet<string>();
        for (var i = 0; i < 10; i++)
        {
            var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
            hosts.Add(result.Target.Url.Host);
        }

        Assert.Contains("a", hosts);
        Assert.Contains("b", hosts);
    }

    [Fact]
    public async Task AllUnhealthy_ReturnsNoHealthyUpstreamAvailable()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var b = UpstreamTarget.Create("http://b:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a, b]));

        actor.Tell(new MarkUpstreamUnhealthy(a.Url));
        actor.Tell(new MarkUpstreamUnhealthy(b.Url));
        await Task.Delay(100);

        var result = await actor.Ask<NoHealthyUpstreamAvailable>(SelectUpstream.Instance, AskTimeout);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateUpstreams_ReplacesRoutingTable()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a]));

        var b = UpstreamTarget.Create("http://b:80");
        actor.Tell(new UpdateUpstreams([b]));
        await Task.Delay(100);

        for (var i = 0; i < 5; i++)
        {
            var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
            Assert.Equal("b", result.Target.Url.Host);
        }
    }

    [Fact]
    public async Task UpdateUpstreams_ResetsHealthState()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var b = UpstreamTarget.Create("http://b:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a, b]));

        // Mark both unhealthy
        actor.Tell(new MarkUpstreamUnhealthy(a.Url));
        actor.Tell(new MarkUpstreamUnhealthy(b.Url));
        await Task.Delay(100);

        // Update with same upstreams — health should reset
        actor.Tell(new UpdateUpstreams([a, b]));
        await Task.Delay(100);

        var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DuplicateUnhealthy_DoesNotRebuildTwice()
    {
        var a = UpstreamTarget.Create("http://a:80");
        var b = UpstreamTarget.Create("http://b:80");
        var actor = _system.ActorOf(LoadBalancerActor.CreateProps([a, b]));

        // Send duplicate unhealthy — should not cause issues
        actor.Tell(new MarkUpstreamUnhealthy(a.Url));
        actor.Tell(new MarkUpstreamUnhealthy(a.Url));
        await Task.Delay(100);

        // Should still work, only 'b' available
        var result = await actor.Ask<UpstreamSelected>(SelectUpstream.Instance, AskTimeout);
        Assert.Equal("b", result.Target.Url.Host);
    }
}
