using Schleusenwerk.Persistence;

namespace Schleusenwerk.Startup;

internal sealed class EnvironmentConfigInitializer : IHostedLifecycleService
{
    private readonly IConfigurationStore _store;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EnvironmentConfigInitializer> _logger;

    public EnvironmentConfigInitializer(
        IConfigurationStore store,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<EnvironmentConfigInitializer> logger)
    {
        _store = store;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartingAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StartAsync(CancellationToken ct) => ApplySettingsOverridesAsync(ct);

    public Task StartedAsync(CancellationToken ct) => ApplyDomainOverridesAsync(ct);

    public Task StoppingAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task ApplySettingsOverridesAsync(CancellationToken ct)
    {
        var settings = await _store.GetSettingsAsync(ct);
        var changed = false;

        var stage = _configuration["STAGE"];
        if (!string.IsNullOrWhiteSpace(stage))
        {
            var parsed = stage.ToLowerInvariant() switch
            {
                "local" => AcmeStage.Local,
                "staging" => AcmeStage.Staging,
                "production" => AcmeStage.Production,
                _ => (AcmeStage?)null,
            };

            if (parsed.HasValue && parsed.Value != settings.Stage)
            {
                settings = settings with { Stage = parsed.Value };
                changed = true;
                _logger.LogInformation("STAGE set to {Stage} from environment", parsed.Value);
            }
        }

        var email = _configuration["ACME_EMAIL"];
        if (!string.IsNullOrWhiteSpace(email) && email != settings.AcmeEmail)
        {
            settings = settings with { AcmeEmail = email };
            changed = true;
            _logger.LogInformation("ACME_EMAIL set from environment");
        }

        var dnsProvider = _configuration["LEGO_DNS_PROVIDER"];
        if (!string.IsNullOrWhiteSpace(dnsProvider) && dnsProvider != settings.DnsProvider)
        {
            settings = settings with { DnsProvider = dnsProvider };
            changed = true;
            _logger.LogInformation("LEGO_DNS_PROVIDER set to {Provider} from environment", dnsProvider);
        }

        if (changed)
        {
            await _store.UpdateSettingsAsync(settings, ct);
        }
    }

    private async Task ApplyDomainOverridesAsync(CancellationToken ct)
    {
        var envConfig = EnvironmentVariableProvider.Load();
        if (envConfig is null)
        {
            return;
        }

        _logger.LogInformation("Applying {Count} domain(s) from DOMAINS environment variable", envConfig.Entries.Count);
        var configService = _serviceProvider.GetRequiredService<IConfigurationService>();
        await EnvironmentVariableProvider.ApplyOverridesAsync(envConfig, configService, ct);
    }
}
