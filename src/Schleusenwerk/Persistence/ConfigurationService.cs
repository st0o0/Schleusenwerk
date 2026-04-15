using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Service layer that routes configuration queries and commands to the
/// <see cref="ConfigurationPersistenceActor"/> via the Ask pattern.
/// Returns Result types for error handling without exceptions.
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
    private readonly IActorRef _configActor;
    private readonly TimeSpan _timeout;

    public ConfigurationService(IReadOnlyActorRegistry registry, TimeSpan? timeout = null)
    {
        _configActor = registry.Get<ConfigurationPersistenceActor>();
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<ConfigurationResult<ConfigurationSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = await _configActor.Ask<ConfigurationSnapshot>(
            GetConfiguration.Instance, _timeout, cancellationToken);
        return new ConfigurationResult<ConfigurationSnapshot>.Success(result);
    }

    public async Task<ConfigurationResult<DomainConfigResult>> GetByDomainAsync(
        DomainName domainName, CancellationToken cancellationToken = default)
    {
        var result = await _configActor.Ask<object>(
            new GetDomainByName(domainName), _timeout, cancellationToken);

        return result switch
        {
            DomainConfigResult domainResult => new ConfigurationResult<DomainConfigResult>.Success(domainResult),
            ConfigurationCommandNack nack => new ConfigurationResult<DomainConfigResult>.Failure(nack.Reason),
            _ => new ConfigurationResult<DomainConfigResult>.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> AddDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(new AddDomain(config), cancellationToken);
    }

    public async Task<ConfigurationResult> UpdateDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(new UpdateDomain(config), cancellationToken);
    }

    public async Task<ConfigurationResult> RemoveDomainAsync(DomainName domainName, CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(new RemoveDomain(domainName), cancellationToken);
    }

    public async Task<ConfigurationResult> AddUpstreamAsync(
        DomainName domainName, UpstreamTarget upstream, CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(new AddUpstream(domainName, upstream), cancellationToken);
    }

    public async Task<ConfigurationResult> RemoveUpstreamAsync(
        DomainName domainName, UpstreamUrl upstreamUrl, CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(new RemoveUpstream(domainName, upstreamUrl), cancellationToken);
    }

    public async Task<ConfigurationResult<ProxySettings>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _configActor.Ask<ProxySettings>(
            GetSettings.Instance, _timeout, cancellationToken);
        return new ConfigurationResult<ProxySettings>.Success(result);
    }

    public async Task<ConfigurationResult> UpdateSettingsAsync(
        ProxySettings settings, CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(new UpdateSettings(settings), cancellationToken);
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

    private async Task<ConfigurationResult> SendCommandAsync(object command, CancellationToken cancellationToken)
    {
        var result = await _configActor.Ask<object>(command, _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }
}
