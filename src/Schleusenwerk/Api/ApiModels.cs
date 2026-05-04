namespace Schleusenwerk.Api;

public sealed record CommandResultDto(bool Success, string? ErrorMessage = null)
{
    public static CommandResultDto Ok() => new(true);
    public static CommandResultDto Fail(string reason) => new(false, reason);
}

public sealed record RouteSummaryDto(
    string Domain,
    bool ForceHttps,
    string Source,
    int TimeoutSeconds,
    string TlsMode,
    bool WebSocketEnabled,
    IReadOnlyList<UpstreamInfoDto> Upstreams);

public sealed record UpstreamInfoDto(string Url, int Weight);

public sealed record RouteDetailDto(
    string Domain,
    bool ForceHttps,
    int TimeoutSeconds,
    string Source,
    string TlsMode,
    bool WebSocketEnabled,
    IReadOnlyList<UpstreamInfoDto> Upstreams,
    IReadOnlyList<UpstreamHealthEntryDto> Health);

public sealed record UpstreamHealthEntryDto(string Url, bool IsHealthy);

public sealed record AddRouteRequestDto(
    string Domain,
    bool ForceHttps = false,
    int TimeoutSeconds = 30,
    string TlsMode = "letsencrypt",
    bool WebSocketEnabled = false,
    string? FirstUpstreamUrl = null);

public sealed record UpdateRouteRequestDto(
    bool ForceHttps,
    int TimeoutSeconds,
    bool WebSocketEnabled = false);

public sealed record AddUpstreamRequestDto(
    string Url,
    int Weight = 1);

public sealed record CertificateSummaryDto(
    string Domain,
    string Thumbprint,
    string NotAfter,
    bool IsSelfSigned);

public sealed record CertificateDetailDto(
    string Domain,
    string Thumbprint,
    string NotBefore,
    string NotAfter,
    string Issuer,
    bool IsSelfSigned);

public sealed record ProxyHealthResponseDto(
    int RouteCount,
    int HealthyCount,
    int UnhealthyCount);

public sealed record UpstreamHealthResponseDto(
    string Domain,
    IReadOnlyList<UpstreamHealthEntryDto> Upstreams);

public sealed record ProxyEventDto(
    string Type,
    string Domain,
    string Message,
    bool IsHealthy,
    string UpstreamUrl);

public sealed record DiscoveredContainerDto(
    string Name,
    string Image,
    string Status,
    IReadOnlyDictionary<string, string> Labels,
    string? AssignedDomain,
    string? ConflictReason);

public sealed record ProxySettingsDto(
    string Stage,
    string AcmeEmail,
    string DnsProvider,
    int DefaultRequestTimeoutSeconds,
    int MaxConnectionsPerUpstream,
    bool ForceHttpsGlobally);

public sealed record UpdateSettingsRequestDto(
    string? Stage = null,
    string? AcmeEmail = null,
    string? DnsProvider = null,
    int? DefaultRequestTimeoutSeconds = null,
    int? MaxConnectionsPerUpstream = null,
    bool? ForceHttpsGlobally = null);
