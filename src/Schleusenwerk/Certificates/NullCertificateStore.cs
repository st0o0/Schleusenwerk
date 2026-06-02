using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public sealed class NullCertificateStore : ICertificateStore
{
    public static NullCertificateStore Instance { get; } = new();

    public X509Certificate2? GetCertificate(DomainName domain) => null;

    public void StoreCertificate(DomainName domain, X509Certificate2 certificate) { }

    public bool HasCertificate(DomainName domain) => false;

    public IReadOnlyList<DomainName> ListDomains() => [];
}
