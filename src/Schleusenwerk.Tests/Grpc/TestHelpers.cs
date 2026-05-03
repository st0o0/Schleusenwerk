using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using Schleusenwerk.Certificates;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Tests.Grpc;

internal sealed class FakeServerCallContext : ServerCallContext
{
    public static readonly FakeServerCallContext Instance = new();

    protected override string MethodCore => "Test";
    protected override string HostCore => "localhost";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore { get; } = new();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Metadata ResponseTrailersCore { get; } = new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new("", []);
    protected override string PeerCore => "127.0.0.1";

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}

internal sealed class FakeConfigurationService : IConfigurationService
{
    private readonly List<DomainConfig> _domains = [];
    private readonly Dictionary<DomainName, List<UpstreamTarget>> _upstreams = new();
    private bool _nextCommandFails;
    private string _failReason = "Fake failure";

    public void Seed(DomainConfig config, params UpstreamTarget[] upstreams)
    {
        _domains.Add(config);
        _upstreams[config.DomainName] = upstreams.ToList();
    }

    public void MakeNextCommandFail(string reason = "Fake failure")
    {
        _nextCommandFails = true;
        _failReason = reason;
    }

    public Task<ConfigurationResult<ConfigurationSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new ConfigurationSnapshot
        {
            Domains = _domains.ToList(),
            Upstreams = _upstreams.ToDictionary(
                kvp => kvp.Key.Value,
                kvp => (IReadOnlyList<UpstreamTarget>)kvp.Value.ToList()),
            Certificates = new Dictionary<string, CertificateInfo>(),
            Settings = ProxySettings.Default
        };
        return Task.FromResult<ConfigurationResult<ConfigurationSnapshot>>(
            new ConfigurationResult<ConfigurationSnapshot>.Success(snapshot));
    }

    public Task<ConfigurationResult<DomainConfigResult>> GetByDomainAsync(DomainName domainName, CancellationToken cancellationToken = default)
    {
        var domain = _domains.FirstOrDefault(d => d.DomainName.Equals(domainName));
        if (domain is null)
        {
            return Task.FromResult<ConfigurationResult<DomainConfigResult>>(
                new ConfigurationResult<DomainConfigResult>.Failure("Not found"));
        }

        var ups = _upstreams.GetValueOrDefault(domainName, []);
        return Task.FromResult<ConfigurationResult<DomainConfigResult>>(
            new ConfigurationResult<DomainConfigResult>.Success(new DomainConfigResult(domain, ups)));
    }

    private Task<ConfigurationResult> CommandResult()
    {
        if (_nextCommandFails)
        {
            _nextCommandFails = false;
            return Task.FromResult<ConfigurationResult>(new ConfigurationResult.Failure(_failReason));
        }
        return Task.FromResult<ConfigurationResult>(ConfigurationResult.Success.Instance);
    }

    public Task<ConfigurationResult> AddDomainAsync(DomainConfig config, CancellationToken cancellationToken = default) => CommandResult();
    public Task<ConfigurationResult> UpdateDomainAsync(DomainConfig config, CancellationToken cancellationToken = default) => CommandResult();
    public Task<ConfigurationResult> RemoveDomainAsync(DomainName domainName, CancellationToken cancellationToken = default) => CommandResult();
    public Task<ConfigurationResult> AddUpstreamAsync(DomainName domainName, UpstreamTarget upstream, CancellationToken cancellationToken = default) => CommandResult();
    public Task<ConfigurationResult> RemoveUpstreamAsync(DomainName domainName, UpstreamUrl upstreamUrl, CancellationToken cancellationToken = default) => CommandResult();
    public Task<ConfigurationResult<ProxySettings>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ConfigurationResult<ProxySettings>>(new ConfigurationResult<ProxySettings>.Success(ProxySettings.Default));
    public Task<ConfigurationResult> UpdateSettingsAsync(ProxySettings settings, CancellationToken cancellationToken = default) => CommandResult();
    public Task<ConfigurationResult<string>> ExportAsync(ConfigurationExportOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConfigurationResult<string>>(new ConfigurationResult<string>.Success("{}"));
}

internal sealed class FakeCertificateStore : ICertificateStore
{
    private readonly Dictionary<DomainName, X509Certificate2> _certs = new();

    public void Seed(DomainName domain, X509Certificate2 cert) => _certs[domain] = cert;

    public X509Certificate2? GetCertificate(DomainName domain) =>
        _certs.GetValueOrDefault(domain);

    public void StoreCertificate(DomainName domain, X509Certificate2 certificate) =>
        _certs[domain] = certificate;

    public bool HasCertificate(DomainName domain) => _certs.ContainsKey(domain);

    public IReadOnlyList<DomainName> ListDomains() => _certs.Keys.ToList();
}
