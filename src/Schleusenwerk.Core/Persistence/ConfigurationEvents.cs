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
public sealed record UpstreamAdded(DomainName DomainName, UpstreamTarget Upstream) : IClusterEvent;

/// <summary>
/// An upstream target was removed from a domain.
/// </summary>
public sealed record UpstreamRemoved(DomainName DomainName, UpstreamUrl UpstreamUrl) : IClusterEvent;

/// <summary>
/// Global proxy settings were updated.
/// </summary>
public sealed record SettingsUpdated(ProxySettings Settings);

/// <summary>
/// Published when a new domain is added and needs a TLS certificate.
/// Consumed by the CertificateRenewalActor to trigger ACME provisioning.
/// </summary>
public sealed record CertificateProvisioningRequested(DomainName DomainName) : ICertificateEvent;

/// <summary>
/// Published when a certificate is approaching expiration (14 days or less).
/// </summary>
public sealed record CertificateExpiring(DomainName DomainName) : ICertificateEvent;

/// <summary>
/// Persisted by ConfigurationPersistenceActor when a domain is registered in the index.
/// </summary>
public sealed record DomainRegistered(DomainName DomainName);

/// <summary>
/// Persisted by ConfigurationPersistenceActor when a domain is unregistered from the index.
/// </summary>
public sealed record DomainUnregistered(DomainName DomainName);
