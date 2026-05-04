using Schleusenwerk.Certificates;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Discovery;

/// <summary>
/// Result of successfully parsing Docker container labels into proxy configuration.
/// </summary>
public sealed record ParsedContainerConfig(
    DomainName Domain,
    UpstreamTarget Upstream,
    HealthCheckConfig HealthCheck,
    TlsMode TlsMode
);

/// <summary>
/// Parses schleusenwerk.* Docker labels into proxy configuration objects.
/// </summary>
public static class ContainerLabelParser
{
    public static bool TryParse(
        IDictionary<string, string> labels,
        string? containerIp,
        out ParsedContainerConfig result,
        out string error)
    {
        result = null!;

        if (!labels.TryGetValue("schleusenwerk.domain", out var domainStr) || string.IsNullOrWhiteSpace(domainStr))
        {
            error = "missing schleusenwerk.domain label";
            return false;
        }

        if (!labels.TryGetValue("schleusenwerk.port", out var portStr) || !int.TryParse(portStr, out var port) || port is < 1 or > 65535)
        {
            error = "missing or invalid schleusenwerk.port label";
            return false;
        }

        if (string.IsNullOrWhiteSpace(containerIp))
        {
            error = "container has no IP address";
            return false;
        }

        if (!DomainName.TryParse(domainStr, out var domain))
        {
            error = $"invalid domain name: '{domainStr}'";
            return false;
        }

        var rawUrl = $"http://{containerIp}:{port}";
        if (!UpstreamUrl.TryParse(rawUrl, out var url))
        {
            error = $"could not construct valid upstream URL: '{rawUrl}'";
            return false;
        }

        var upstream = new UpstreamTarget { Url = url };
        var healthCheck = ParseHealthCheckConfig(labels);
        var tlsMode = ParseTlsMode(labels);

        result = new ParsedContainerConfig(domain, upstream, healthCheck, tlsMode);
        error = null!;
        return true;
    }

    private static HealthCheckConfig ParseHealthCheckConfig(IDictionary<string, string> labels)
    {
        var config = new HealthCheckConfig();

        if (labels.TryGetValue("schleusenwerk.healthcheck.path", out var path) && !string.IsNullOrWhiteSpace(path))
        {
            config = config with { HealthEndpoint = path };
        }

        if (labels.TryGetValue("schleusenwerk.healthcheck.interval", out var intervalStr))
        {
            var interval = ParseDuration(intervalStr);
            if (interval.HasValue)
            {
                config = config with { Interval = interval.Value };
            }
        }

        return config;
    }

    private static TlsMode ParseTlsMode(IDictionary<string, string> labels)
    {
        if (!labels.TryGetValue("schleusenwerk.tls", out var tls))
        {
            return TlsMode.LetsEncrypt;
        }

        return tls.ToLowerInvariant() switch
        {
            "letsencrypt" => TlsMode.LetsEncrypt,
            "dns" => TlsMode.Dns,
            "selfsigned" => TlsMode.SelfSigned,
            "custom" => TlsMode.Custom,
            _ => TlsMode.LetsEncrypt,
        };
    }

    // Parses simple duration strings like "30s", "1m", "5m30s".
    internal static TimeSpan? ParseDuration(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var s = input.Trim();
        double totalSeconds = 0;
        var i = 0;

        while (i < s.Length)
        {
            var j = i;
            while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.'))
                j++;

            if (j == i || j >= s.Length)
                return null;

            if (!double.TryParse(s[i..j], out var value))
                return null;

            var unit = s[j];
            totalSeconds += unit switch
            {
                's' => value,
                'm' => value * 60,
                'h' => value * 3600,
                _ => -1
            };

            if (totalSeconds < 0)
                return null;

            i = j + 1;
        }

        return totalSeconds > 0 ? TimeSpan.FromSeconds(totalSeconds) : null;
    }
}
