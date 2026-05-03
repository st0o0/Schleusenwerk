using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

internal sealed class CertificateClient : ICertificateClient
{
    private readonly CertificateService.CertificateServiceClient _client;

    public CertificateClient(GrpcChannel channel) =>
        _client = new CertificateService.CertificateServiceClient(channel);

    public async Task<IReadOnlyList<CertificateSummary>> ListCertificatesAsync(CancellationToken ct = default)
    {
        var response = await _client.ListCertificatesAsync(new Empty(), cancellationToken: ct);
        return response.Certificates;
    }

    public async Task<CertificateDetail> GetCertificateAsync(string domain, CancellationToken ct = default)
    {
        return await _client.GetCertificateAsync(new GetCertificateRequest { Domain = domain }, cancellationToken: ct);
    }

    public async Task<CommandResult> ProvisionCertificateAsync(string domain, CancellationToken ct = default)
    {
        return await _client.ProvisionCertificateAsync(
            new ProvisionCertificateRequest { Domain = domain }, cancellationToken: ct);
    }
}
