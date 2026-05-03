using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

internal sealed class CertesAcmeClient : IAcmeClient
{
    private readonly IConfigurationStore _configStore;
    private readonly string _accountKeyPath;
    private readonly ILogger<CertesAcmeClient> _logger;

    private AcmeContext? _acme;
    private IOrderContext? _currentOrder;

    public CertesAcmeClient(
        IConfigurationStore configStore,
        string certsPath,
        ILogger<CertesAcmeClient> logger)
    {
        _configStore = configStore;
        _accountKeyPath = Path.Combine(certsPath, "acme-account.pem");
        _logger = logger;
    }

    public async Task<AcmeOrderResult> StartOrderAsync(DomainName domain, CancellationToken ct)
    {
        await EnsureAccountAsync(ct);

        _currentOrder = await _acme!.NewOrder(new[] { domain.Value });
        var authz = (await _currentOrder.Authorizations()).First();
        var challenge = await authz.Http();

        return new AcmeOrderResult(challenge.Token, challenge.KeyAuthz);
    }

    public async Task<X509Certificate2> CompleteOrderAsync(DomainName domain, CancellationToken ct)
    {
        if (_currentOrder is null)
        {
            throw new InvalidOperationException("No pending order. Call StartOrderAsync first.");
        }

        var authz = (await _currentOrder.Authorizations()).First();
        var challenge = await authz.Http();

        var challengeResult = await challenge.Validate();
        var maxWait = TimeSpan.FromSeconds(120);
        var waited = TimeSpan.Zero;
        var pollInterval = TimeSpan.FromSeconds(2);

        while (challengeResult.Status == Certes.Acme.Resource.ChallengeStatus.Pending ||
               challengeResult.Status == Certes.Acme.Resource.ChallengeStatus.Processing)
        {
            if (waited >= maxWait)
            {
                throw new TimeoutException($"ACME challenge validation timed out after {maxWait.TotalSeconds}s");
            }

            await Task.Delay(pollInterval, ct);
            waited += pollInterval;
            challengeResult = await challenge.Resource();
        }

        if (challengeResult.Status != Certes.Acme.Resource.ChallengeStatus.Valid)
        {
            throw new InvalidOperationException(
                $"ACME challenge failed with status {challengeResult.Status}: {challengeResult.Error?.Detail}");
        }

        var privateKey = KeyFactory.NewKey(Certes.KeyAlgorithm.RS256);
        var certChain = await _currentOrder.Generate(
            new CsrInfo { CommonName = domain.Value }, privateKey);

        var pfxBytes = certChain.ToPfx(privateKey).Build(domain.Value, "");
        _currentOrder = null;

        return new X509Certificate2(pfxBytes, "", X509KeyStorageFlags.Exportable);
    }

    private async Task EnsureAccountAsync(CancellationToken ct)
    {
        if (_acme is not null)
        {
            return;
        }

        var settings = await _configStore.GetSettingsAsync(ct);
        var acmeUri = settings.Stage switch
        {
            AcmeStage.Staging => WellKnownServers.LetsEncryptStagingV2,
            AcmeStage.Production => WellKnownServers.LetsEncryptV2,
            _ => throw new InvalidOperationException($"ACME not available for stage {settings.Stage}")
        };

        if (File.Exists(_accountKeyPath))
        {
            var pem = await File.ReadAllTextAsync(_accountKeyPath, ct);
            var accountKey = KeyFactory.FromPem(pem);
            _acme = new AcmeContext(acmeUri, accountKey);
            _logger.LogInformation("Loaded existing ACME account from {Path}", _accountKeyPath);
        }
        else
        {
            _acme = new AcmeContext(acmeUri);
            var email = string.IsNullOrWhiteSpace(settings.AcmeEmail) ? null : settings.AcmeEmail;
            await _acme.NewAccount(email, termsOfServiceAgreed: true);

            var keyPem = _acme.AccountKey.ToPem();
            var dir = Path.GetDirectoryName(_accountKeyPath)!;
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_accountKeyPath, keyPem, ct);
            _logger.LogInformation("Created new ACME account, key saved to {Path}", _accountKeyPath);
        }
    }
}
