using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

public interface ICertificateClient
{
    Task<IReadOnlyList<CertificateSummary>> ListCertificatesAsync(CancellationToken ct = default);
    Task<CertificateDetail> GetCertificateAsync(string domain, CancellationToken ct = default);
    Task<CommandResult> ProvisionCertificateAsync(string domain, CancellationToken ct = default);
}
