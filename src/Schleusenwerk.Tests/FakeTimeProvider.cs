namespace Schleusenwerk.Tests;

internal sealed class FakeTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long GetTimestamp() => _timestamp;

    public override long TimestampFrequency => 1_000;

    public void Advance(TimeSpan duration)
    {
        _timestamp += (long)(duration.TotalMilliseconds);
    }
}
