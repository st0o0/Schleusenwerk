using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class FileCertificateStoreSpec : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"cert-test-{Guid.NewGuid():N}");

    public FileCertificateStoreSpec()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private FileCertificateStore CreateStore() => new(_tempDir);

    private static X509Certificate2 CreateTestCert(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
    }

    [Fact(Timeout = 5000)]
    public void GetCertificate_should_return_null_when_not_stored()
    {
        var store = CreateStore();

        var result = store.GetCertificate(DomainName.Parse("example.com"));

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void StoreCertificate_should_persist_and_retrieve()
    {
        var store = CreateStore();
        var domain = DomainName.Parse("example.com");
        using var cert = CreateTestCert("example.com");

        store.StoreCertificate(domain, cert);
        using var loaded = store.GetCertificate(domain);

        Assert.NotNull(loaded);
        Assert.Equal(cert.Thumbprint, loaded.Thumbprint);
    }

    [Fact(Timeout = 5000)]
    public void HasCertificate_should_return_true_after_store()
    {
        var store = CreateStore();
        var domain = DomainName.Parse("example.com");
        using var cert = CreateTestCert("example.com");

        store.StoreCertificate(domain, cert);

        Assert.True(store.HasCertificate(domain));
    }

    [Fact(Timeout = 5000)]
    public void HasCertificate_should_return_false_when_not_stored()
    {
        var store = CreateStore();

        Assert.False(store.HasCertificate(DomainName.Parse("missing.com")));
    }

    [Fact(Timeout = 5000)]
    public void ListDomains_should_return_all_stored_domains()
    {
        var store = CreateStore();
        using var cert1 = CreateTestCert("a.com");
        using var cert2 = CreateTestCert("b.com");

        store.StoreCertificate(DomainName.Parse("a.com"), cert1);
        store.StoreCertificate(DomainName.Parse("b.com"), cert2);

        var domains = store.ListDomains();
        Assert.Equal(2, domains.Count);
        Assert.Contains(DomainName.Parse("a.com"), domains);
        Assert.Contains(DomainName.Parse("b.com"), domains);
    }

    [Fact(Timeout = 5000)]
    public void GetCertificate_should_survive_new_store_instance()
    {
        var domain = DomainName.Parse("example.com");
        using var cert = CreateTestCert("example.com");

        var store1 = CreateStore();
        store1.StoreCertificate(domain, cert);

        var store2 = CreateStore();
        using var loaded = store2.GetCertificate(domain);

        Assert.NotNull(loaded);
        Assert.Equal(cert.Thumbprint, loaded.Thumbprint);
    }
}
