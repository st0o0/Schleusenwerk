using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Certificates;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/certificates")]
public sealed class CertificateController : ControllerBase
{
    private readonly ICertificateStore _store;
    private readonly IReadOnlyActorRegistry _registry;

    public CertificateController(ICertificateStore store, IReadOnlyActorRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<CertificateSummaryDto>> ListCertificates()
    {
        var certs = new List<CertificateSummaryDto>();
        foreach (var domain in _store.ListDomains())
        {
            var cert = _store.GetCertificate(domain);
            if (cert is not null)
            {
                certs.Add(DomainModelMapper.ToCertificateSummary(domain, cert));
            }
        }
        return Ok(certs);
    }

    [HttpGet("{domain}")]
    public ActionResult<CertificateDetailDto> GetCertificate(string domain)
    {
        var domainName = DomainName.Parse(domain);
        var cert = _store.GetCertificate(domainName);

        if (cert is null)
        {
            return NotFound();
        }

        return Ok(DomainModelMapper.ToCertificateDetail(domainName, cert));
    }

    [HttpPost("{domain}/provision")]
    public ActionResult<CommandResultDto> ProvisionCertificate(string domain)
    {
        var eventHub = _registry.Get<EventHub>();
        eventHub.Tell(new CertificateProvisioningRequested(DomainName.Parse(domain)), ActorRefs.NoSender);
        return Ok(CommandResultDto.Ok());
    }

    [HttpPost("{domain}/upload")]
    [Consumes("multipart/form-data")]
    public ActionResult<CommandResultDto> UploadCertificate(
        string domain,
        IFormFile file,
        IFormFile? keyFile = null,
        [FromForm] string? password = null)
    {
        var domainName = DomainName.Parse(domain);

        try
        {
            using var cert = LoadCertificate(file, keyFile, password);

            if (cert.NotAfter < DateTime.UtcNow)
            {
                return Ok(CommandResultDto.Fail("Certificate is expired"));
            }

            if (!CertificateMatchesDomain(cert, domain))
            {
                return Ok(CommandResultDto.Fail($"Certificate does not match domain '{domain}'"));
            }

            if (!cert.HasPrivateKey)
            {
                return Ok(CommandResultDto.Fail("Certificate has no private key"));
            }

            _store.StoreCertificate(domainName, cert);
            return Ok(CommandResultDto.Ok());
        }
        catch (Exception ex)
        {
            return Ok(CommandResultDto.Fail($"Failed to load certificate: {ex.Message}"));
        }
    }

    private static X509Certificate2 LoadCertificate(IFormFile file, IFormFile? keyFile, string? password)
    {
        using var stream = new MemoryStream();
        file.CopyTo(stream);
        var bytes = stream.ToArray();

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is ".pfx" or ".p12")
        {
            return new X509Certificate2(bytes, password, X509KeyStorageFlags.Exportable);
        }

        var certPem = System.Text.Encoding.UTF8.GetString(bytes);

        if (keyFile is null)
        {
            throw new InvalidOperationException("PEM certificate requires a separate key file");
        }

        using var keyStream = new MemoryStream();
        keyFile.CopyTo(keyStream);
        var keyPem = System.Text.Encoding.UTF8.GetString(keyStream.ToArray());

        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    private static bool CertificateMatchesDomain(X509Certificate2 cert, string domain)
    {
        var san = cert.Extensions["2.5.29.17"];
        if (san is not null)
        {
            var sanString = san.Format(false);
            if (sanString.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        return string.Equals(cn, domain, StringComparison.OrdinalIgnoreCase);
    }
}
