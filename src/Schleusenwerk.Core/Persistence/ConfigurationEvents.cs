using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// A domain configuration was added.
/// </summary>
public sealed record DomainAdded(DomainConfig Config) : IClusterEvent;

/// <summary>
/// A domain configuration was updated.
/// </summary>
public sealed record DomainUpdated(DomainConfig Config) : IClusterEvent;

/// <summary>
/// A domain was removed.
/// </summary>
public sealed record DomainRemoved(DomainName DomainName) : IClusterEvent;

/// <summary>
/// An upstream target was added to a domain.
/// </summary>
public sealed record UpstreamAdded(DomainName DomainName, UpstreamTarget Upstream);

/// <summary>
/// An upstream target was removed from a domain.
/// </summary>
public sealed record UpstreamRemoved(DomainName DomainName, UpstreamUrl UpstreamUrl);

/// <summary>
/// Global proxy settings were updated.
/// </summary>
public sealed record SettingsUpdated(ProxySettings Settings);

/// <summary>
/// Published when a new domain is added and needs a TLS certificate.
/// Consumed by the CertificateRenewalActor to trigger ACME provisioning.
/// </summary>
public sealed record CertificateProvisioningRequested(DomainName DomainName) : IClusterEvent;
