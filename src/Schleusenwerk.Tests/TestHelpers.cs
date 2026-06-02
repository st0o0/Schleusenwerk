namespace Schleusenwerk.Tests;

internal static class TestHelpers
{
    internal static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? message = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(3);
        var p = pollInterval ?? TimeSpan.FromMilliseconds(50);
        using var cts = new CancellationTokenSource(t);

        while (!condition())
        {
            if (cts.Token.IsCancellationRequested)
            {
                throw new TimeoutException(message ?? "Condition was not met within timeout");
            }

            await Task.Delay(p, cts.Token);
        }
    }

    internal static async Task<T> WaitUntilAsync<T>(
        Func<Task<T>> query,
        Func<T, bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? message = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(3);
        var p = pollInterval ?? TimeSpan.FromMilliseconds(50);
        using var cts = new CancellationTokenSource(t);
        T result = default!;

        while (true)
        {
            result = await query();
            if (condition(result))
            {
                return result;
            }

            if (cts.Token.IsCancellationRequested)
            {
                throw new TimeoutException(message ?? $"Condition was not met within timeout. Last result: {result}");
            }

            await Task.Delay(p, cts.Token);
        }
    }
}
