using Schleusenwerk.HealthCheck;
using Xunit;

namespace Schleusenwerk.Tests.HealthCheck;

public sealed class HealthCheckConfigSpec
{
    [Fact(Timeout = 5000)]
    public void Defaults_should_use_thirty_second_interval()
    {
        var config = new HealthCheckConfig();

        Assert.Equal(TimeSpan.FromSeconds(30), config.Interval);
    }

    [Fact(Timeout = 5000)]
    public void Defaults_should_require_three_failures_before_unhealthy()
    {
        var config = new HealthCheckConfig();

        Assert.Equal(3, config.UnhealthyThreshold);
    }

    [Fact(Timeout = 5000)]
    public void Defaults_should_require_two_successes_before_healthy()
    {
        var config = new HealthCheckConfig();

        Assert.Equal(2, config.HealthyThreshold);
    }

    [Fact(Timeout = 5000)]
    public void Defaults_should_use_root_health_endpoint()
    {
        var config = new HealthCheckConfig();

        Assert.Equal("/", config.HealthEndpoint);
    }

    [Fact(Timeout = 5000)]
    public void Defaults_should_use_ten_second_timeout()
    {
        var config = new HealthCheckConfig();

        Assert.Equal(TimeSpan.FromSeconds(10), config.Timeout);
    }

    [Fact(Timeout = 5000)]
    public void Defaults_should_use_head_method()
    {
        var config = new HealthCheckConfig();

        Assert.True(config.UseHead);
    }

    [Fact(Timeout = 5000)]
    public void Config_should_be_customizable()
    {
        var config = new HealthCheckConfig
        {
            Interval = TimeSpan.FromSeconds(15),
            UnhealthyThreshold = 5,
            HealthyThreshold = 3,
            HealthEndpoint = "/healthz",
            Timeout = TimeSpan.FromSeconds(5),
            UseHead = false,
        };

        Assert.Equal(TimeSpan.FromSeconds(15), config.Interval);
        Assert.Equal(5, config.UnhealthyThreshold);
        Assert.Equal(3, config.HealthyThreshold);
        Assert.Equal("/healthz", config.HealthEndpoint);
        Assert.Equal(TimeSpan.FromSeconds(5), config.Timeout);
        Assert.False(config.UseHead);
    }
}