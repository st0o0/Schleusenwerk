using Schleusenwerk.Persistence;

namespace Schleusenwerk.Startup;

internal sealed class EnvironmentConfigInitializer : IHostedService
{
    private readonly IConfigurationStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EnvironmentConfigInitializer> _logger;

    public EnvironmentConfigInitializer(
        IConfigurationStore store,
        IConfiguration configuration,
        ILogger<EnvironmentConfigInitializer> logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
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

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
