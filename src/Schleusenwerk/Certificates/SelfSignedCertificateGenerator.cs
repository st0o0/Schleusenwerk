using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public static class SelfSignedCertificateGenerator
{
    public static X509Certificate2 Generate(DomainName domain, TimeSpan? validity = null)
    {
        var effectiveValidity = validity ?? TimeSpan.FromDays(365);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={domain.Value}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(domain.Value);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.Add(effectiveValidity));

        var pfxData = cert.Export(X509ContentType.Pfx);
#pragma warning disable SYSLIB0057
        return new X509Certificate2(pfxData, (string?)null, X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
    }
}
