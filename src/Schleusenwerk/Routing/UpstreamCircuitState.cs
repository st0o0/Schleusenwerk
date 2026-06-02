namespace Schleusenwerk.Routing;

public enum CircuitStatus
{
    Closed,
    Open,
    HalfOpen,
}

public sealed class UpstreamCircuitState
{
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);
    private const int FailureThreshold = 3;

    private readonly UpstreamUrl _url;
    private readonly TimeSpan _baseCooldown;
    private readonly TimeProvider _timeProvider;
    private CircuitStatus _status = CircuitStatus.Closed;
    private long _openedAtTimestamp;
    private TimeSpan _currentCooldown;

    public UpstreamCircuitState(UpstreamUrl url, TimeSpan baseCooldown, TimeProvider? timeProvider = null)
    {
        _url = url;
        _baseCooldown = baseCooldown;
        _currentCooldown = baseCooldown;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int ConsecutiveFailures { get; private set; }

    public CircuitStatus Status
    {
        get
        {
            if (_status == CircuitStatus.Open && _timeProvider.GetElapsedTime(_openedAtTimestamp) >= _currentCooldown)
            {
                _status = CircuitStatus.HalfOpen;
            }
            return _status;
        }
    }

    public bool IsAvailable => Status != CircuitStatus.Open;

    public void RecordFailure()
    {
        if (Status == CircuitStatus.HalfOpen)
        {
            _currentCooldown = TimeSpan.FromTicks(Math.Min(_currentCooldown.Ticks * 2, MaxCooldown.Ticks));
            TransitionToOpen();
            return;
        }

        ConsecutiveFailures++;
        if (ConsecutiveFailures >= FailureThreshold)
        {
            _currentCooldown = _baseCooldown;
            TransitionToOpen();
        }
    }

    public void RecordSuccess()
    {
        _status = CircuitStatus.Closed;
        ConsecutiveFailures = 0;
        _currentCooldown = _baseCooldown;
    }

    public void ForceOpen()
    {
        _currentCooldown = _baseCooldown;
        TransitionToOpen();
    }

    public void ForceClose()
    {
        _status = CircuitStatus.Closed;
        ConsecutiveFailures = 0;
        _currentCooldown = _baseCooldown;
    }

    private void TransitionToOpen()
    {
        _status = CircuitStatus.Open;
        _openedAtTimestamp = _timeProvider.GetTimestamp();
    }
}
