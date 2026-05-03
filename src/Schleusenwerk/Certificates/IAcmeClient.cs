using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public sealed record AcmeOrderResult(string Token, string KeyAuthorization);

public interface IAcmeClient
{
    Task<AcmeOrderResult> StartOrderAsync(DomainName domain, CancellationToken ct = default);
    Task<X509Certificate2> CompleteOrderAsync(DomainName domain, CancellationToken ct = default);
}
