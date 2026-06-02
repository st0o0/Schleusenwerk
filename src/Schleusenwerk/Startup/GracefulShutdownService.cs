using Schleusenwerk.Forwarding;

namespace Schleusenwerk.Startup;

internal sealed class GracefulShutdownService : IHostedService
{
    private readonly ConnectionTracker _tracker;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GracefulShutdownService> _logger;
    private readonly TimeSpan _drainTimeout;

    public GracefulShutdownService(
        ConnectionTracker tracker,
        IHostApplicationLifetime lifetime,
        ILogger<GracefulShutdownService> logger,
        IConfiguration configuration)
    {
        _tracker = tracker;
        _lifetime = lifetime;
        _logger = logger;

        var seconds = double.TryParse(configuration["Proxy:DrainTimeoutSeconds"], out var s) ? s : 30;
        _drainTimeout = TimeSpan.FromSeconds(seconds);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStopping.Register(OnStopping);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnStopping()
    {
        _logger.LogInformation("Shutdown initiated — draining {Count} active connections (timeout: {Timeout}s)",
            _tracker.ActiveCount, _drainTimeout.TotalSeconds);
        _tracker.StartDraining();
        _tracker.WaitForDrainAsync(_drainTimeout).GetAwaiter().GetResult();
        _logger.LogInformation("Drain complete — {Count} connections remaining", _tracker.ActiveCount);
    }
}
