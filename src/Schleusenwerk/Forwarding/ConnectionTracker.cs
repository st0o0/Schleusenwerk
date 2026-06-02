namespace Schleusenwerk.Forwarding;

internal sealed class ConnectionTracker
{
    private int _activeCount;
    private volatile bool _isDraining;
    private readonly TaskCompletionSource _drainComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ActiveCount => Volatile.Read(ref _activeCount);

    public void StartDraining() => _isDraining = true;

    public async Task WaitForDrainAsync(TimeSpan timeout)
    {
        if (ActiveCount == 0)
        {
            return;
        }

        await Task.WhenAny(_drainComplete.Task, Task.Delay(timeout));
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (_isDraining)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        Interlocked.Increment(ref _activeCount);
        try
        {
            await next(context);
        }
        finally
        {
            if (Interlocked.Decrement(ref _activeCount) == 0 && _isDraining)
            {
                _drainComplete.TrySetResult();
            }
        }
    }
}
