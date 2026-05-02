using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public sealed class FileCertificateStore : ICertificateStore
{
    private readonly string _basePath;

    public FileCertificateStore(string basePath)
    {
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public X509Certificate2? GetCertificate(DomainName domain)
    {
        var path = GetCertPath(domain);
        if (!File.Exists(path))
        {
            return null;
        }

        return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.Exportable);
    }

    public void StoreCertificate(DomainName domain, X509Certificate2 certificate)
    {
        var path = GetCertPath(domain);
        var pfxBytes = certificate.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfxBytes);
    }

    public bool HasCertificate(DomainName domain)
    {
        return File.Exists(GetCertPath(domain));
    }

    public IReadOnlyList<DomainName> ListDomains()
    {
        if (!Directory.Exists(_basePath))
        {
            return [];
        }

        return Directory.GetFiles(_basePath, "*.pfx")
            .Select(f => DomainName.Parse(Path.GetFileNameWithoutExtension(f)))
            .ToList();
    }

    private string GetCertPath(DomainName domain) =>
        Path.Combine(_basePath, $"{domain.Value}.pfx");
}
