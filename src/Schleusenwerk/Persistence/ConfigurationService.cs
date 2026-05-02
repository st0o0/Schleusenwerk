using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

public sealed class ConfigurationService : IConfigurationService
{
    private readonly IActorRef _domainRegion;
    private readonly IConfigurationStore _store;
    private readonly TimeSpan _timeout;

    public ConfigurationService(IReadOnlyActorRegistry registry, IConfigurationStore store, TimeSpan? timeout = null)
    {
        _domainRegion = registry.Get<DomainEntityActor>();
        _store = store;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<ConfigurationResult<ConfigurationSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var domains = await _store.GetAllDomainsAsync(cancellationToken);
        var settings = await _store.GetSettingsAsync(cancellationToken);

        var snapshot = new ConfigurationSnapshot
        {
            Domains = domains.ToList(),
            Upstreams = new Dictionary<string, IReadOnlyList<UpstreamTarget>>(),
            Certificates = new Dictionary<string, CertificateInfo>(),
            Settings = settings,
        };

        return new ConfigurationResult<ConfigurationSnapshot>.Success(snapshot);
    }

    public async Task<ConfigurationResult<DomainConfigResult>> GetByDomainAsync(
        DomainName domainName, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new GetDomainConfig { Domain = domainName.Value }, _timeout, cancellationToken);

        return result switch
        {
            DomainConfigResult domainResult => new ConfigurationResult<DomainConfigResult>.Success(domainResult),
            ConfigurationCommandNack nack => new ConfigurationResult<DomainConfigResult>.Failure(nack.Reason),
            _ => new ConfigurationResult<DomainConfigResult>.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> AddDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new AddDomain(config), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> UpdateDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new UpdateDomain(config), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> RemoveDomainAsync(DomainName domainName, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new RemoveDomain(domainName), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> AddUpstreamAsync(
        DomainName domainName, UpstreamTarget upstream, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new AddUpstream(domainName, upstream), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> RemoveUpstreamAsync(
        DomainName domainName, UpstreamUrl upstreamUrl, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new RemoveUpstream(domainName, upstreamUrl), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult<ProxySettings>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _store.GetSettingsAsync(cancellationToken);
        return new ConfigurationResult<ProxySettings>.Success(settings);
    }

    public async Task<ConfigurationResult> UpdateSettingsAsync(
        ProxySettings settings, CancellationToken cancellationToken = default)
    {
        await _store.UpdateSettingsAsync(settings, cancellationToken);
        return ConfigurationResult.Success.Instance;
    }

    public async Task<ConfigurationResult<string>> ExportAsync(
        ConfigurationExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshotResult = await GetAllAsync(cancellationToken);

        if (snapshotResult is ConfigurationResult<ConfigurationSnapshot>.Failure failure)
        {
            return new ConfigurationResult<string>.Failure(failure.Error);
        }

        var snapshot = ((ConfigurationResult<ConfigurationSnapshot>.Success)snapshotResult).Value;
        var json = ConfigurationExporter.ToJson(snapshot, options);
        return new ConfigurationResult<string>.Success(json);
    }
}
