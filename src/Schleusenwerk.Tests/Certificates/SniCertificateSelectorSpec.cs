using Schleusenwerk.Certificates;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class SniCertificateSelectorSpec : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"sni-test-{Guid.NewGuid():N}");
    private readonly FileCertificateStore _store;
    private readonly SniCertificateSelector _selector;

    public SniCertificateSelectorSpec()
    {
        Directory.CreateDirectory(_tempDir);
        _store = new FileCertificateStore(_tempDir);
        _selector = new SniCertificateSelector(_store);
    }

    public void Dispose()
    {
        _selector.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact(Timeout = 5000)]
    public void Select_should_return_stored_certificate_for_known_domain()
    {
        var domain = DomainName.Parse("example.com");
        using var cert = SelfSignedCertificateGenerator.Generate(domain);
        _store.StoreCertificate(domain, cert);

        using var selected = _selector.Select("example.com");

        Assert.NotNull(selected);
        Assert.Equal(cert.Thumbprint, selected.Thumbprint);
    }

    [Fact(Timeout = 5000)]
    public void Select_should_return_fallback_for_unknown_domain()
    {
        using var selected = _selector.Select("unknown.com");

        Assert.NotNull(selected);
        Assert.Contains("CN=localhost", selected.Subject);
    }

    [Fact(Timeout = 5000)]
    public void Select_should_return_fallback_for_null_hostname()
    {
        using var selected = _selector.Select(null);

        Assert.NotNull(selected);
    }
}
