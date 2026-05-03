using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

internal sealed class HealthClient : IHealthClient
{
    private readonly HealthService.HealthServiceClient _client;

    public HealthClient(GrpcChannel channel) =>
        _client = new HealthService.HealthServiceClient(channel);

    public async Task<ProxyHealthResponse> GetHealthAsync(CancellationToken ct = default)
    {
        return await _client.GetHealthAsync(new Empty(), cancellationToken: ct);
    }

    public async Task<UpstreamHealthResponse> GetUpstreamHealthAsync(string domain, CancellationToken ct = default)
    {
        return await _client.GetUpstreamHealthAsync(
            new GetUpstreamHealthRequest { Domain = domain }, cancellationToken: ct);
    }
}
