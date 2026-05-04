using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

public interface IConfigurationStore
{
    Task<ProxySettings> GetSettingsAsync(CancellationToken ct = default);
    Task UpdateSettingsAsync(ProxySettings settings, CancellationToken ct = default);

    Task<IReadOnlyList<DomainConfig>> GetAllDomainsAsync(CancellationToken ct = default);
    Task<DomainConfig?> GetDomainAsync(DomainName name, CancellationToken ct = default);
    Task UpsertDomainAsync(DomainConfig config, CancellationToken ct = default);
    Task RemoveDomainAsync(DomainName name, CancellationToken ct = default);
}
