using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Api;

internal static class DomainModelMapper
{
    public static RouteSummaryDto ToRouteSummary(DomainConfig config, IReadOnlyList<UpstreamTarget> upstreams) =>
        new(
            Domain: config.DomainName.Value,
            ForceHttps: config.ForceHttps,
            Source: "manual",
            TimeoutSeconds: (int)config.RequestTimeout.TotalSeconds,
            TlsMode: config.TlsMode.ToString().ToLowerInvariant(),
            Upstreams: upstreams.Select(ToUpstreamInfo).ToList());

    public static UpstreamInfoDto ToUpstreamInfo(UpstreamTarget target) =>
        new(Url: target.Url.Value.ToString(), Weight: target.Weight);

    public static RouteDetailDto ToRouteDetail(
        DomainConfig config,
        IReadOnlyList<UpstreamTarget> upstreams,
        IReadOnlyList<UpstreamHealthStatus> health) =>
        new(
            Domain: config.DomainName.Value,
            ForceHttps: config.ForceHttps,
            TimeoutSeconds: (int)config.RequestTimeout.TotalSeconds,
            Source: "manual",
            TlsMode: config.TlsMode.ToString().ToLowerInvariant(),
            Upstreams: upstreams.Select(ToUpstreamInfo).ToList(),
            Health: health.Select(h => new UpstreamHealthEntryDto(h.Url.Value.ToString(), h.IsHealthy)).ToList());

    public static CertificateSummaryDto ToCertificateSummary(DomainName domain, X509Certificate2 cert) =>
        new(
            Domain: domain.Value,
            Thumbprint: cert.Thumbprint,
            NotAfter: cert.NotAfter.ToString("O"),
            IsSelfSigned: cert.Issuer == cert.Subject);

    public static CertificateDetailDto ToCertificateDetail(DomainName domain, X509Certificate2 cert) =>
        new(
            Domain: domain.Value,
            Thumbprint: cert.Thumbprint,
            NotBefore: cert.NotBefore.ToString("O"),
            NotAfter: cert.NotAfter.ToString("O"),
            Issuer: cert.Issuer,
            IsSelfSigned: cert.Issuer == cert.Subject);

    public static bool CanMapToProxyEvent(IClusterEvent evt)
        => evt is DomainConfigured or DomainDeactivated or UpstreamHealthChanged
           or CertificateProvisioningRequested or CertificateExpiring;

    public static ProxyEventDto ToProxyEvent(IClusterEvent evt) => evt switch
    {
        DomainConfigured e => new ProxyEventDto("RouteUpdated", e.Config.DomainName.Value, "", true, ""),
        DomainDeactivated e => new ProxyEventDto("RouteRemoved", e.DomainName.Value, "", false, ""),
        UpstreamHealthChanged e => new ProxyEventDto("UpstreamHealthChanged", "", "", e.IsHealthy, e.Url.Value.ToString()),
        CertificateProvisioningRequested e => new ProxyEventDto("CertificateProvisioned", e.DomainName.Value, "", true, ""),
        CertificateExpiring e => new ProxyEventDto("CertificateExpiring", e.DomainName.Value, "", false, ""),
        _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.GetType().Name, "Unmappable event")
    };
}
