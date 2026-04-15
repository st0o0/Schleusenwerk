using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Parses DOMAINS and STAGE environment variables in https-portal format.
/// Entries use "->" for upstream proxying and "=>" for redirects.
/// Multiple entries are comma-separated.
/// </summary>
public static class EnvironmentVariableProvider
{
    public const string DomainsVariable = "DOMAINS";
    public const string StageVariable = "STAGE";

    /// <summary>
    /// A parsed domain entry from the DOMAINS environment variable.
    /// Either a proxy route (domain -> upstream) or a redirect (domain => target).
    /// </summary>
    public sealed record DomainEntry
    {
        public required DomainName DomainName { get; init; }
        public UpstreamTarget? Upstream { get; init; }
        public string? RedirectTarget { get; init; }
        public bool IsRedirect => RedirectTarget is not null;
    }

    /// <summary>
    /// Result of loading environment variables.
    /// </summary>
    public sealed record EnvironmentConfig
    {
        public required IReadOnlyList<DomainEntry> Entries { get; init; }
        public required AcmeStage Stage { get; init; }
    }

    /// <summary>
    /// Loads configuration from environment variables.
    /// Returns null if the DOMAINS variable is not set.
    /// </summary>
    public static EnvironmentConfig? Load()
    {
        var domainsRaw = Environment.GetEnvironmentVariable(DomainsVariable);
        if (string.IsNullOrWhiteSpace(domainsRaw))
        {
            return null;
        }

        var entries = ParseDomains(domainsRaw);
        var stage = ParseStage(Environment.GetEnvironmentVariable(StageVariable));

        return new EnvironmentConfig
        {
            Entries = entries,
            Stage = stage,
        };
    }

    /// <summary>
    /// Parses a DOMAINS string in https-portal format.
    /// Entries are comma-separated. Each entry is either:
    /// <c>domain -> http://upstream:port</c> (proxy) or
    /// <c>domain => https://target</c> (redirect).
    /// </summary>
    public static IReadOnlyList<DomainEntry> ParseDomains(string domainsValue)
    {
        if (string.IsNullOrWhiteSpace(domainsValue))
        {
            return [];
        }

        var entries = new List<DomainEntry>();
        var segments = domainsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            entries.Add(ParseEntry(segment));
        }

        return entries;
    }

    /// <summary>
    /// Parses the STAGE environment variable into an <see cref="AcmeStage"/>.
    /// Defaults to <see cref="AcmeStage.Local"/> if not set or unrecognized.
    /// </summary>
    public static AcmeStage ParseStage(string? stageValue)
    {
        if (string.IsNullOrWhiteSpace(stageValue))
        {
            return AcmeStage.Local;
        }

        return stageValue.Trim().ToLowerInvariant() switch
        {
            "local" => AcmeStage.Local,
            "staging" => AcmeStage.Staging,
            "production" => AcmeStage.Production,
            _ => throw new FormatException($"Invalid STAGE value: '{stageValue}'. Expected 'local', 'staging', or 'production'."),
        };
    }

    /// <summary>
    /// Applies the parsed environment configuration to the configuration service as overrides.
    /// Call this after loading persisted/JSON config so that env vars take the highest priority.
    /// Existing domains are updated; new domains are added. The ACME stage is applied to global settings.
    /// </summary>
    public static async Task ApplyOverridesAsync(
        EnvironmentConfig config,
        IConfigurationService service,
        CancellationToken cancellationToken = default)
    {
        foreach (var entry in config.Entries)
        {
            var domainConfig = BuildDomainConfig(entry);

            var addResult = await service.AddDomainAsync(domainConfig, cancellationToken);
            if (addResult is ConfigurationResult.Failure)
            {
                await service.UpdateDomainAsync(domainConfig, cancellationToken);
            }

            if (!entry.IsRedirect && entry.Upstream is not null)
            {
                await service.AddUpstreamAsync(entry.DomainName, entry.Upstream, cancellationToken);
            }
        }

        var settingsResult = await service.GetSettingsAsync(cancellationToken);
        if (settingsResult is ConfigurationResult<ProxySettings>.Success settingsSuccess)
        {
            var updated = settingsSuccess.Value with { Stage = config.Stage };
            await service.UpdateSettingsAsync(updated, cancellationToken);
        }
    }

    private static DomainConfig BuildDomainConfig(DomainEntry entry)
    {
        if (entry.IsRedirect)
        {
            return new DomainConfig
            {
                DomainName = entry.DomainName,
                HttpRedirect = RedirectMode.PermanentRedirect,
                RedirectUrl = entry.RedirectTarget is not null ? new Uri(entry.RedirectTarget) : null,
            };
        }

        return new DomainConfig
        {
            DomainName = entry.DomainName,
        };
    }

    private static DomainEntry ParseEntry(string entry)
    {
        // Try redirect operator first (=>) since it's more specific
        var redirectIndex = entry.IndexOf("=>", StringComparison.Ordinal);
        if (redirectIndex >= 0)
        {
            return ParseRedirectEntry(entry, redirectIndex);
        }

        // Try proxy operator (->)
        var proxyIndex = entry.IndexOf("->", StringComparison.Ordinal);
        if (proxyIndex >= 0)
        {
            return ParseProxyEntry(entry, proxyIndex);
        }

        throw new FormatException(
            $"Invalid DOMAINS entry: '{entry}'. Expected 'domain -> upstream' or 'domain => redirect'.");
    }

    private static DomainEntry ParseProxyEntry(string entry, int operatorIndex)
    {
        var domainPart = entry[..operatorIndex].Trim();
        var upstreamPart = entry[(operatorIndex + 2)..].Trim();

        if (string.IsNullOrEmpty(domainPart))
        {
            throw new FormatException($"Missing domain name in DOMAINS entry: '{entry}'.");
        }

        if (string.IsNullOrEmpty(upstreamPart))
        {
            throw new FormatException($"Missing upstream URL in DOMAINS entry: '{entry}'.");
        }

        return new DomainEntry
        {
            DomainName = DomainName.Parse(domainPart),
            Upstream = UpstreamTarget.Create(upstreamPart),
        };
    }

    private static DomainEntry ParseRedirectEntry(string entry, int operatorIndex)
    {
        var domainPart = entry[..operatorIndex].Trim();
        var targetPart = entry[(operatorIndex + 2)..].Trim();

        if (string.IsNullOrEmpty(domainPart))
        {
            throw new FormatException($"Missing domain name in DOMAINS entry: '{entry}'.");
        }

        if (string.IsNullOrEmpty(targetPart))
        {
            throw new FormatException($"Missing redirect target in DOMAINS entry: '{entry}'.");
        }

        // Validate that the redirect target is a valid URL
        if (!Uri.TryCreate(targetPart, UriKind.Absolute, out var targetUri))
        {
            throw new FormatException($"Invalid redirect target URL '{targetPart}' in DOMAINS entry: '{entry}'.");
        }

        if (targetUri.Scheme is not ("http" or "https"))
        {
            throw new FormatException(
                $"Redirect target must use http or https scheme, got '{targetUri.Scheme}' in DOMAINS entry: '{entry}'.");
        }

        return new DomainEntry
        {
            DomainName = DomainName.Parse(domainPart),
            RedirectTarget = targetUri.ToString(),
        };
    }
}
