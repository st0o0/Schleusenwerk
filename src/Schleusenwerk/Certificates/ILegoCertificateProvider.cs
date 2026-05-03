using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public interface ILegoCertificateProvider
{
    Task<X509Certificate2> ProvisionAsync(DomainName domain, TlsMode mode, CancellationToken ct = default);
    Task<X509Certificate2> RenewAsync(DomainName domain, TlsMode mode, CancellationToken ct = default);
}
