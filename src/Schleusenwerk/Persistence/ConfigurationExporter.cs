using System.Text.Json;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Options controlling what data is included in a configuration export.
/// </summary>
public sealed record ConfigurationExportOptions
{
    /// <summary>
    /// When true, certificate metadata (thumbprints, issuers) is excluded from the export.
    /// </summary>
    public bool ExcludeSensitiveData { get; init; }

    public static ConfigurationExportOptions Default => new();
}

/// <summary>
/// Converts <see cref="ConfigurationSnapshot"/> to a portable JSON document
/// suitable for backup, migration, and roundtrip import.
/// </summary>
public static class ConfigurationExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Converts a configuration snapshot to an export document.
    /// </summary>
    public static ConfigExportDocument ToExportDocument(
        ConfigurationSnapshot snapshot,
        ConfigurationExportOptions? options = null)
    {
        var opts = options ?? ConfigurationExportOptions.Default;

        var domainEntries = snapshot.Domains.Select(domain =>
        {
            var domainName = domain.DomainName.Value;

            var upstreams = snapshot.Upstreams.TryGetValue(domainName, out var targets)
                ? targets.Select(ToUpstreamEntry).ToList()
                : [];

            CertificateExportEntry? certificate = null;
            if (!opts.ExcludeSensitiveData
                && snapshot.Certificates.TryGetValue(domainName, out var certInfo))
            {
                certificate = ToCertificateEntry(certInfo);
            }

            return new DomainExportEntry
            {
                DomainName = domainName,
                HttpRedirect = domain.HttpRedirect.ToString(),
                RedirectUrl = domain.RedirectUrl?.ToString(),
                ForceHttps = domain.ForceHttps,
                PreserveHostHeader = domain.PreserveHostHeader,
                RequestTimeoutSeconds = domain.RequestTimeout.TotalSeconds,
                Upstreams = upstreams,
                Certificate = certificate,
            };
        }).ToList();

        return new ConfigExportDocument
        {
            Domains = domainEntries,
            Settings = ToSettingsEntry(snapshot.Settings),
        };
    }

    /// <summary>
    /// Serializes a configuration snapshot to a JSON string.
    /// </summary>
    public static string ToJson(
        ConfigurationSnapshot snapshot,
        ConfigurationExportOptions? options = null)
    {
        var document = ToExportDocument(snapshot, options);
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    /// <summary>
    /// Deserializes a JSON string back to an export document.
    /// </summary>
    public static ConfigExportDocument? FromJson(string json)
    {
        return JsonSerializer.Deserialize<ConfigExportDocument>(json, JsonOptions);
    }

    /// <summary>
    /// Converts an export document back to a <see cref="ConfigurationSnapshot"/>.
    /// Used for roundtrip import.
    /// </summary>
    public static ConfigurationSnapshot ToSnapshot(ConfigExportDocument document)
    {
        var domains = document.Domains.Select(entry => new DomainConfig
        {
            DomainName = DomainName.Parse(entry.DomainName),
            HttpRedirect = Enum.Parse<RedirectMode>(entry.HttpRedirect),
            RedirectUrl = entry.RedirectUrl is not null ? new Uri(entry.RedirectUrl) : null,
            ForceHttps = entry.ForceHttps,
            PreserveHostHeader = entry.PreserveHostHeader,
            RequestTimeout = TimeSpan.FromSeconds(entry.RequestTimeoutSeconds),
        }).ToList();

        var upstreams = document.Domains.ToDictionary(
            entry => entry.DomainName,
            entry => (IReadOnlyList<UpstreamTarget>)entry.Upstreams.Select(u =>
                new UpstreamTarget
                {
                    Url = UpstreamUrl.Parse(u.Url),
                    Weight = u.Weight,
                    MaxConnections = u.MaxConnections,
                }).ToList());

        var certificates = document.Domains
            .Where(entry => entry.Certificate is not null)
            .ToDictionary(
                entry => entry.DomainName,
                entry => new CertificateInfo
                {
                    Thumbprint = entry.Certificate!.Thumbprint,
                    NotBefore = entry.Certificate.NotBefore,
                    NotAfter = entry.Certificate.NotAfter,
                    Issuer = entry.Certificate.Issuer,
                    IsSelfSigned = entry.Certificate.IsSelfSigned,
                });

        return new ConfigurationSnapshot
        {
            Domains = domains,
            Upstreams = upstreams,
            Certificates = certificates,
            Settings = new ProxySettings
            {
                DefaultRequestTimeout = TimeSpan.FromSeconds(document.Settings.DefaultRequestTimeoutSeconds),
                MaxConnectionsPerUpstream = document.Settings.MaxConnectionsPerUpstream,
                ForceHttpsGlobally = document.Settings.ForceHttpsGlobally,
                SnapshotInterval = document.Settings.SnapshotInterval,
                Stage = Enum.Parse<AcmeStage>(document.Settings.Stage),
            },
        };
    }

    private static UpstreamExportEntry ToUpstreamEntry(UpstreamTarget target)
    {
        return new UpstreamExportEntry
        {
            Url = target.Url.ToString(),
            Weight = target.Weight,
            MaxConnections = target.MaxConnections,
        };
    }

    private static CertificateExportEntry ToCertificateEntry(CertificateInfo cert)
    {
        return new CertificateExportEntry
        {
            Thumbprint = cert.Thumbprint,
            NotBefore = cert.NotBefore,
            NotAfter = cert.NotAfter,
            Issuer = cert.Issuer,
            IsSelfSigned = cert.IsSelfSigned,
        };
    }

    private static SettingsExportEntry ToSettingsEntry(ProxySettings settings)
    {
        return new SettingsExportEntry
        {
            DefaultRequestTimeoutSeconds = settings.DefaultRequestTimeout.TotalSeconds,
            MaxConnectionsPerUpstream = settings.MaxConnectionsPerUpstream,
            ForceHttpsGlobally = settings.ForceHttpsGlobally,
            SnapshotInterval = settings.SnapshotInterval,
            Stage = settings.Stage.ToString(),
        };
    }
}
