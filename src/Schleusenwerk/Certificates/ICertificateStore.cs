using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public interface ICertificateStore
{
    X509Certificate2? GetCertificate(DomainName domain);
    void StoreCertificate(DomainName domain, X509Certificate2 certificate);
    bool HasCertificate(DomainName domain);
    IReadOnlyList<DomainName> ListDomains();
}
