using Microsoft.AspNetCore.Http;
using Schleusenwerk.Forwarding;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class ConnectionTrackerSpec
{
    [Fact(Timeout = 5000)]
    public async Task ConnectionTracker_should_track_active_request_count()
    {
        var tracker = new ConnectionTracker();
        var tcs = new TaskCompletionSource();

        Assert.Equal(0, tracker.ActiveCount);

        var requestTask = tracker.InvokeAsync(
            new DefaultHttpContext(),
            _ => tcs.Task);

        Assert.Equal(1, tracker.ActiveCount);
        tcs.SetResult();
        await requestTask;
        Assert.Equal(0, tracker.ActiveCount);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionTracker_should_return_503_when_draining()
    {
        var tracker = new ConnectionTracker();
        tracker.StartDraining();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await tracker.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal(503, context.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionTracker_should_wait_for_active_requests_to_complete()
    {
        var tracker = new ConnectionTracker();
        var tcs = new TaskCompletionSource();

        var requestTask = tracker.InvokeAsync(
            new DefaultHttpContext(),
            _ => tcs.Task);

        tracker.StartDraining();
        var drainTask = tracker.WaitForDrainAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.False(drainTask.IsCompleted);

        tcs.SetResult();
        await requestTask;
        await drainTask;

        Assert.Equal(0, tracker.ActiveCount);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionTracker_should_timeout_drain_when_requests_hang()
    {
        var tracker = new ConnectionTracker();
        var tcs = new TaskCompletionSource();

        _ = tracker.InvokeAsync(new DefaultHttpContext(), _ => tcs.Task);

        tracker.StartDraining();
        await tracker.WaitForDrainAsync(TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, tracker.ActiveCount);
        tcs.SetResult();
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionTracker_should_decrement_even_when_next_throws()
    {
        var tracker = new ConnectionTracker();

        var context = new DefaultHttpContext();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tracker.InvokeAsync(context, _ => throw new InvalidOperationException("boom")));

        Assert.Equal(0, tracker.ActiveCount);
    }
}
