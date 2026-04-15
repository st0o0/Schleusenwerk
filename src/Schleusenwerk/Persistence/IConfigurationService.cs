using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Service layer for configuration queries and commands.
/// Abstracts away direct actor interaction via Ask pattern.
/// Returns Result types instead of throwing exceptions.
/// </summary>
public interface IConfigurationService
{
    Task<ConfigurationResult<ConfigurationSnapshot>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ConfigurationResult<DomainConfigResult>> GetByDomainAsync(DomainName domainName, CancellationToken cancellationToken = default);
    Task<ConfigurationResult> AddDomainAsync(DomainConfig config, CancellationToken cancellationToken = default);
    Task<ConfigurationResult> UpdateDomainAsync(DomainConfig config, CancellationToken cancellationToken = default);
    Task<ConfigurationResult> RemoveDomainAsync(DomainName domainName, CancellationToken cancellationToken = default);
    Task<ConfigurationResult> AddUpstreamAsync(DomainName domainName, UpstreamTarget upstream, CancellationToken cancellationToken = default);
    Task<ConfigurationResult> RemoveUpstreamAsync(DomainName domainName, UpstreamUrl upstreamUrl, CancellationToken cancellationToken = default);
    Task<ConfigurationResult<ProxySettings>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<ConfigurationResult> UpdateSettingsAsync(ProxySettings settings, CancellationToken cancellationToken = default);
    Task<ConfigurationResult<string>> ExportAsync(ConfigurationExportOptions? options = null, CancellationToken cancellationToken = default);
}
