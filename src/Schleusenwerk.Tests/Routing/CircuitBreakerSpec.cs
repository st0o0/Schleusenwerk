using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Routing;

public sealed class CircuitBreakerSpec
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_start_closed()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);
        Assert.Equal(CircuitStatus.Closed, state.Status);
        Assert.True(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_open_after_threshold_failures()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);
        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();
        Assert.Equal(CircuitStatus.Open, state.Status);
        Assert.False(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_reset_failure_count_on_success()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);
        state.RecordFailure();
        state.RecordFailure();
        state.RecordSuccess();
        Assert.Equal(CircuitStatus.Closed, state.Status);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_transition_to_half_open_after_cooldown()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), TimeSpan.FromMilliseconds(50));
        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();
        Assert.Equal(CircuitStatus.Open, state.Status);
        Thread.Sleep(100);
        Assert.Equal(CircuitStatus.HalfOpen, state.Status);
        Assert.True(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_close_on_success_when_half_open()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), TimeSpan.FromMilliseconds(50));
        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();
        Thread.Sleep(100);
        Assert.Equal(CircuitStatus.HalfOpen, state.Status);
        state.RecordSuccess();
        Assert.Equal(CircuitStatus.Closed, state.Status);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_reopen_with_doubled_cooldown_on_failure_when_half_open()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), TimeSpan.FromMilliseconds(50));
        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();
        Thread.Sleep(100);
        Assert.Equal(CircuitStatus.HalfOpen, state.Status);
        state.RecordFailure();
        Assert.Equal(CircuitStatus.Open, state.Status);
        // Should NOT be half-open after 50ms anymore (doubled to 100ms)
        Thread.Sleep(60);
        Assert.Equal(CircuitStatus.Open, state.Status);
        Thread.Sleep(60);
        Assert.Equal(CircuitStatus.HalfOpen, state.Status);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_force_open_on_health_check_failure()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);
        state.ForceOpen();
        Assert.Equal(CircuitStatus.Open, state.Status);
        Assert.False(state.IsAvailable);
    }

    [Fact(Timeout = 5000)]
    public void CircuitBreaker_should_force_close_on_health_check_recovery()
    {
        var state = new UpstreamCircuitState(UpstreamUrl.Parse("http://a:8080"), Cooldown);
        state.RecordFailure();
        state.RecordFailure();
        state.RecordFailure();
        Assert.Equal(CircuitStatus.Open, state.Status);
        state.ForceClose();
        Assert.Equal(CircuitStatus.Closed, state.Status);
        Assert.True(state.IsAvailable);
    }
}
