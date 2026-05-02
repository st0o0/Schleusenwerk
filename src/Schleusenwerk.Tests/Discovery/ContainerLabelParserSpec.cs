using Schleusenwerk.Discovery;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Discovery;

public sealed class ContainerLabelParserSpec
{
    private static Dictionary<string, string> ValidLabels(
        string domain = "app.example.com",
        string port = "8080") =>
        new()
        {
            ["schleusenwerk.domain"] = domain,
            ["schleusenwerk.port"] = port,
        };

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_parse_valid_labels_into_correct_domain_and_upstream()
    {
        var result = ContainerLabelParser.TryParse(ValidLabels(), "172.17.0.5", out var config, out _);

        Assert.True(result);
        Assert.Equal("app.example.com", config.Domain.Value);
        Assert.Equal("http://172.17.0.5:8080/", config.Upstream.Url.Value.ToString());
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_fail_when_domain_label_is_missing()
    {
        var labels = new Dictionary<string, string> { ["schleusenwerk.port"] = "8080" };

        var result = ContainerLabelParser.TryParse(labels, "172.17.0.5", out _, out var error);

        Assert.False(result);
        Assert.Contains("schleusenwerk.domain", error);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_fail_when_port_label_is_missing()
    {
        var labels = new Dictionary<string, string> { ["schleusenwerk.domain"] = "app.example.com" };

        var result = ContainerLabelParser.TryParse(labels, "172.17.0.5", out _, out var error);

        Assert.False(result);
        Assert.Contains("schleusenwerk.port", error);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_fail_when_port_is_not_a_number()
    {
        var labels = ValidLabels(port: "notaport");

        var result = ContainerLabelParser.TryParse(labels, "172.17.0.5", out _, out var error);

        Assert.False(result);
        Assert.Contains("schleusenwerk.port", error);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_fail_when_port_is_out_of_range()
    {
        var labels = ValidLabels(port: "99999");

        var result = ContainerLabelParser.TryParse(labels, "172.17.0.5", out _, out var error);

        Assert.False(result);
        Assert.Contains("schleusenwerk.port", error);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_fail_when_container_ip_is_null()
    {
        var result = ContainerLabelParser.TryParse(ValidLabels(), null, out _, out var error);

        Assert.False(result);
        Assert.Contains("IP", error);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_fail_when_domain_is_invalid()
    {
        var labels = ValidLabels(domain: "not a valid domain!");

        var result = ContainerLabelParser.TryParse(labels, "172.17.0.5", out _, out var error);

        Assert.False(result);
        Assert.Contains("domain", error);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_use_default_health_check_config_when_no_health_labels()
    {
        ContainerLabelParser.TryParse(ValidLabels(), "10.0.0.1", out var config, out _);

        Assert.Equal("/", config.HealthCheck.HealthEndpoint);
        Assert.Equal(TimeSpan.FromSeconds(30), config.HealthCheck.Interval);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_parse_healthcheck_path_label()
    {
        var labels = ValidLabels();
        labels["schleusenwerk.healthcheck.path"] = "/health";

        ContainerLabelParser.TryParse(labels, "10.0.0.1", out var config, out _);

        Assert.Equal("/health", config.HealthCheck.HealthEndpoint);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_parse_healthcheck_interval_label()
    {
        var labels = ValidLabels();
        labels["schleusenwerk.healthcheck.interval"] = "30s";

        ContainerLabelParser.TryParse(labels, "10.0.0.1", out var config, out _);

        Assert.Equal(TimeSpan.FromSeconds(30), config.HealthCheck.Interval);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_parse_minute_duration()
    {
        var interval = ContainerLabelParser.ParseDuration("2m");

        Assert.Equal(TimeSpan.FromMinutes(2), interval);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_parse_combined_duration()
    {
        var interval = ContainerLabelParser.ParseDuration("1m30s");

        Assert.Equal(TimeSpan.FromSeconds(90), interval);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_return_null_for_invalid_duration()
    {
        var interval = ContainerLabelParser.ParseDuration("invalid");

        Assert.Null(interval);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_ignore_unknown_labels()
    {
        var labels = ValidLabels();
        labels["schleusenwerk.unknown"] = "value";
        labels["unrelated.label"] = "other";

        var result = ContainerLabelParser.TryParse(labels, "10.0.0.1", out _, out _);

        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void ContainerLabelParser_should_return_false_for_empty_labels()
    {
        var result = ContainerLabelParser.TryParse(new Dictionary<string, string>(), "10.0.0.1", out _, out var error);

        Assert.False(result);
        Assert.Contains("schleusenwerk.domain", error);
    }
}
