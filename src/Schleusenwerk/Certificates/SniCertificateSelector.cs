using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public sealed class SniCertificateSelector : IDisposable
{
    private readonly ICertificateStore _store;
    private readonly Lazy<X509Certificate2> _fallback;

    public SniCertificateSelector(ICertificateStore store)
    {
        _store = store;
        _fallback = new Lazy<X509Certificate2>(
            () => SelfSignedCertificateGenerator.Generate(DomainName.Parse("localhost")));
    }

    public X509Certificate2 Select(string? hostname)
    {
        if (!string.IsNullOrEmpty(hostname) && DomainName.TryParse(hostname, out var domain))
        {
            var cert = _store.GetCertificate(domain);
            if (cert is not null)
            {
                return cert;
            }
        }

        return _fallback.Value;
    }

    public void Dispose()
    {
        if (_fallback.IsValueCreated)
        {
            _fallback.Value.Dispose();
        }
    }
}
