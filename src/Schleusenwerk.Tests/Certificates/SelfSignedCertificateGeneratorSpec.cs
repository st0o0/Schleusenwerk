using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class SelfSignedCertificateGeneratorSpec
{
    [Fact(Timeout = 5000)]
    public void Generate_should_return_certificate_with_correct_subject()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        Assert.Contains("CN=example.com", cert.Subject);
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_return_certificate_with_private_key()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        Assert.True(cert.HasPrivateKey);
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_include_san_extension()
    {
        var domain = DomainName.Parse("app.example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        var sanExtension = cert.Extensions
            .OfType<X509Extension>()
            .FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");

        Assert.NotNull(sanExtension);
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_have_valid_date_range()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        Assert.True(cert.NotBefore <= DateTime.UtcNow);
        Assert.True(cert.NotAfter > DateTime.UtcNow.AddDays(300));
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_have_server_auth_eku()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        Assert.NotNull(eku);
        Assert.Contains(eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>(),
            o => o.Value == "1.3.6.1.5.5.7.3.1");
    }
}
