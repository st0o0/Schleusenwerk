using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.Contracts;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Grpc;

internal static class ProtoMapper
{
    public static CommandResult Ok() => new() { Success = true };

    public static CommandResult Fail(string reason) => new() { Success = false, ErrorMessage = reason };

    public static RouteSummary ToRouteSummary(DomainConfig config, IReadOnlyList<UpstreamTarget> upstreams)
    {
        var summary = new RouteSummary
        {
            Domain = config.DomainName.Value,
            ForceHttps = config.ForceHttps,
            Source = "manual",
            TimeoutSeconds = (int)config.RequestTimeout.TotalSeconds
        };
        summary.Upstreams.AddRange(upstreams.Select(ToUpstreamInfo));
        return summary;
    }

    public static UpstreamInfo ToUpstreamInfo(UpstreamTarget target) => new()
    {
        Url = target.Url.Value.ToString(),
        Weight = target.Weight
    };

    public static RouteDetail ToRouteDetail(
        DomainConfig config,
        IReadOnlyList<UpstreamTarget> upstreams,
        IReadOnlyList<UpstreamHealthStatus> health)
    {
        var detail = new RouteDetail
        {
            Domain = config.DomainName.Value,
            ForceHttps = config.ForceHttps,
            TimeoutSeconds = (int)config.RequestTimeout.TotalSeconds,
            Source = "manual"
        };
        detail.Upstreams.AddRange(upstreams.Select(ToUpstreamInfo));
        detail.Health.AddRange(health.Select(h => new UpstreamHealthEntry
        {
            Url = h.Url.Value.ToString(),
            IsHealthy = h.IsHealthy
        }));
        return detail;
    }

    public static CertificateSummary ToCertificateSummary(DomainName domain, X509Certificate2 cert) => new()
    {
        Domain = domain.Value,
        Thumbprint = cert.Thumbprint,
        NotAfter = cert.NotAfter.ToString("O"),
        IsSelfSigned = cert.Issuer == cert.Subject
    };

    public static CertificateDetail ToCertificateDetail(DomainName domain, X509Certificate2 cert) => new()
    {
        Domain = domain.Value,
        Thumbprint = cert.Thumbprint,
        NotBefore = cert.NotBefore.ToString("O"),
        NotAfter = cert.NotAfter.ToString("O"),
        Issuer = cert.Issuer,
        IsSelfSigned = cert.Issuer == cert.Subject
    };

    public static bool CanMapToProxyEvent(IClusterEvent evt) 
        => evt is DomainConfigured or DomainDeactivated or UpstreamHealthChanged or CertificateProvisioningRequested;

    public static ProxyEvent ToProxyEvent(IClusterEvent evt) => evt switch
    {
        DomainConfigured e => new ProxyEvent
        {
            Type = EventType.RouteUpdated,
            Domain = e.Config.DomainName.Value
        },
        DomainDeactivated e => new ProxyEvent
        {
            Type = EventType.RouteRemoved,
            Domain = e.DomainName.Value
        },
        UpstreamHealthChanged e => new ProxyEvent
        {
            Type = EventType.UpstreamHealthChanged,
            UpstreamUrl = e.Url.Value.ToString(),
            IsHealthy = e.IsHealthy
        },
        CertificateProvisioningRequested e => new ProxyEvent
        {
            Type = EventType.CertificateProvisioned,
            Domain = e.DomainName.Value
        },
        _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.GetType().Name, "Unmappable event")
    };
}
