using System.Text.Json.Serialization;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Top-level JSON document for configuration export/import.
/// Defines the wire format for backup and migration of proxy configuration.
/// </summary>
public sealed record ConfigExportDocument
{
    [JsonPropertyName("domains")]
    public required IReadOnlyList<DomainExportEntry> Domains { get; init; }

    [JsonPropertyName("settings")]
    public required SettingsExportEntry Settings { get; init; }
}

/// <summary>
/// Per-domain entry in the export document, combining domain config,
/// upstreams, and optional certificate metadata.
/// </summary>
public sealed record DomainExportEntry
{
    [JsonPropertyName("domainName")]
    public required string DomainName { get; init; }

    [JsonPropertyName("httpRedirect")]
    public required string HttpRedirect { get; init; }

    [JsonPropertyName("redirectUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RedirectUrl { get; init; }

    [JsonPropertyName("forceHttps")]
    public required bool ForceHttps { get; init; }

    [JsonPropertyName("preserveHostHeader")]
    public required bool PreserveHostHeader { get; init; }

    [JsonPropertyName("webSocketEnabled")]
    public bool WebSocketEnabled { get; init; }

    [JsonPropertyName("requestTimeoutSeconds")]
    public required double RequestTimeoutSeconds { get; init; }

    [JsonPropertyName("upstreams")]
    public required IReadOnlyList<UpstreamExportEntry> Upstreams { get; init; }

    [JsonPropertyName("certificate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CertificateExportEntry? Certificate { get; init; }
}

/// <summary>
/// Upstream target entry in the export document.
/// </summary>
public sealed record UpstreamExportEntry
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("weight")]
    public required int Weight { get; init; }

    [JsonPropertyName("maxConnections")]
    public required int MaxConnections { get; init; }
}

/// <summary>
/// Certificate metadata entry in the export document.
/// Excluded when sensitive data filtering is enabled.
/// </summary>
public sealed record CertificateExportEntry
{
    [JsonPropertyName("thumbprint")]
    public required string Thumbprint { get; init; }

    [JsonPropertyName("notBefore")]
    public required DateTimeOffset NotBefore { get; init; }

    [JsonPropertyName("notAfter")]
    public required DateTimeOffset NotAfter { get; init; }

    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("isSelfSigned")]
    public required bool IsSelfSigned { get; init; }
}

/// <summary>
/// Global proxy settings entry in the export document.
/// </summary>
public sealed record SettingsExportEntry
{
    [JsonPropertyName("defaultRequestTimeoutSeconds")]
    public required double DefaultRequestTimeoutSeconds { get; init; }

    [JsonPropertyName("maxConnectionsPerUpstream")]
    public required int MaxConnectionsPerUpstream { get; init; }

    [JsonPropertyName("forceHttpsGlobally")]
    public required bool ForceHttpsGlobally { get; init; }

    [JsonPropertyName("snapshotInterval")]
    public required int SnapshotInterval { get; init; }

    [JsonPropertyName("stage")]
    public required string Stage { get; init; }
}
