using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.Certificates;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class CertificateProvisioningActorSpec : PersistenceTestKit
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prov-test-{Guid.NewGuid():N}");
    private int _actorCounter;

    private (IActorRef actor, FileCertificateStore store) CreateActor()
    {
        var id = Interlocked.Increment(ref _actorCounter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-prov-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var store = new FileCertificateStore(_tempDir);
        var configStore = new InMemoryConfigurationStore();
        var configService = new MockConfigurationService();
        var legoProvider = new MockLegoCertificateProvider();

        var actor = Sys.ActorOf(
            Props.Create(() => new CertificateProvisioningActor(store, configStore, configService, legoProvider)),
            $"cert-prov-{id}");

        return (actor, store);
    }

    protected override void AfterAll()
    {
        base.AfterAll();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task CertificateProvisioningActor_should_generate_cert_on_request()
    {
        var (actor, store) = CreateActor();
        var domain = DomainName.Parse("example.com");

        actor.Tell(new CertificateProvisioningRequested(domain));

        await Task.Delay(500);

        Assert.True(store.HasCertificate(domain));
        using var cert = store.GetCertificate(domain);
        Assert.NotNull(cert);
        Assert.Contains("CN=example.com", cert.Subject);
    }

    [Fact(Timeout = 5000)]
    public async Task CertificateProvisioningActor_should_skip_if_cert_already_exists()
    {
        var (actor, store) = CreateActor();
        var domain = DomainName.Parse("already-exists.com");

        using var existingCert = SelfSignedCertificateGenerator.Generate(domain);
        store.StoreCertificate(domain, existingCert);
        var originalThumbprint = existingCert.Thumbprint;

        actor.Tell(new CertificateProvisioningRequested(domain));

        await Task.Delay(500);

        using var loaded = store.GetCertificate(domain);
        Assert.Equal(originalThumbprint, loaded!.Thumbprint);
    }
}

// Mock implementations for testing
internal sealed class InMemoryConfigurationStore : IConfigurationStore
{
    private ProxySettings _settings = ProxySettings.Default;

    public Task<ProxySettings> GetSettingsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_settings);
    }

    public Task UpdateSettingsAsync(ProxySettings settings, CancellationToken ct = default)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DomainConfig>> GetAllDomainsAsync(CancellationToken ct = default)
    {
        return Task.FromResult((IReadOnlyList<DomainConfig>)new List<DomainConfig>());
    }

    public Task<DomainConfig?> GetDomainAsync(DomainName name, CancellationToken ct = default)
    {
        return Task.FromResult((DomainConfig?)null);
    }

    public Task UpsertDomainAsync(DomainConfig config, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveDomainAsync(DomainName name, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class MockConfigurationService : IConfigurationService
{
    public Task<ConfigurationResult<ConfigurationSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult<DomainConfigResult>> GetByDomainAsync(DomainName domain, CancellationToken cancellationToken = default)
    {
        var config = new DomainConfig
        {
            DomainName = domain,
            TlsMode = TlsMode.LetsEncrypt,
        };
        var result = new ConfigurationResult<DomainConfigResult>.Success(new DomainConfigResult(config, (IReadOnlyList<UpstreamTarget>)new List<UpstreamTarget>()));
        return Task.FromResult((ConfigurationResult<DomainConfigResult>)result);
    }

    public Task<ConfigurationResult> AddDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult> UpdateDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult> RemoveDomainAsync(DomainName domainName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult> AddUpstreamAsync(DomainName domainName, UpstreamTarget upstream, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult> RemoveUpstreamAsync(DomainName domainName, UpstreamUrl upstreamUrl, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult<ProxySettings>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult> UpdateSettingsAsync(ProxySettings settings, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ConfigurationResult<string>> ExportAsync(ConfigurationExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}

internal sealed class MockLegoCertificateProvider : ILegoCertificateProvider
{
    public Task<System.Security.Cryptography.X509Certificates.X509Certificate2> ProvisionAsync(DomainName domain, TlsMode mode, CancellationToken ct = default)
    {
        return Task.FromResult(SelfSignedCertificateGenerator.Generate(domain));
    }

    public Task<System.Security.Cryptography.X509Certificates.X509Certificate2> RenewAsync(DomainName domain, TlsMode mode, CancellationToken ct = default)
    {
        return Task.FromResult(SelfSignedCertificateGenerator.Generate(domain));
    }
}
