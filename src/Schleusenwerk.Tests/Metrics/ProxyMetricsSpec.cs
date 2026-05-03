using System.Diagnostics.Metrics;
using Schleusenwerk.Metrics;
using Xunit;

namespace Schleusenwerk.Tests.Metrics;

public sealed class ProxyMetricsSpec
{
    [Fact(Timeout = 5000)]
    public void ProxyMetrics_should_expose_request_counter()
    {
        var metrics = new ProxyMetrics();
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "proxy.requests")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();

        metrics.RecordRequest("example.com", 200);

        Assert.Single(measurements);
        Assert.Equal(1, measurements[0]);
    }

    [Fact(Timeout = 5000)]
    public void ProxyMetrics_should_expose_duration_histogram()
    {
        var metrics = new ProxyMetrics();
        var measurements = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "proxy.request.duration")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();

        metrics.RecordDuration("example.com", "http://backend:8080", 150.5);

        Assert.Single(measurements);
        Assert.Equal(150.5, measurements[0]);
    }

    [Fact(Timeout = 5000)]
    public void ProxyMetrics_should_expose_rate_limit_rejected_counter()
    {
        var metrics = new ProxyMetrics();
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "proxy.rate_limit.rejected")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => measurements.Add(measurement));
        listener.Start();

        metrics.RecordRateLimitRejected("example.com", "1.2.3.4");

        Assert.Single(measurements);
    }
}
