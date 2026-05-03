using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

internal sealed class RouteClient : IRouteClient
{
    private readonly RouteService.RouteServiceClient _client;

    public RouteClient(GrpcChannel channel) =>
        _client = new RouteService.RouteServiceClient(channel);

    public async Task<IReadOnlyList<RouteSummary>> ListRoutesAsync(CancellationToken ct = default)
    {
        var response = await _client.ListRoutesAsync(new Empty(), cancellationToken: ct);
        return response.Routes;
    }

    public async Task<RouteDetail> GetRouteAsync(string domain, CancellationToken ct = default)
    {
        return await _client.GetRouteAsync(new GetRouteRequest { Domain = domain }, cancellationToken: ct);
    }

    public async Task<CommandResult> AddRouteAsync(AddRouteRequest request, CancellationToken ct = default)
    {
        return await _client.AddRouteAsync(request, cancellationToken: ct);
    }

    public async Task<CommandResult> UpdateRouteAsync(UpdateRouteRequest request, CancellationToken ct = default)
    {
        return await _client.UpdateRouteAsync(request, cancellationToken: ct);
    }

    public async Task<CommandResult> DeleteRouteAsync(string domain, CancellationToken ct = default)
    {
        return await _client.DeleteRouteAsync(new DeleteRouteRequest { Domain = domain }, cancellationToken: ct);
    }

    public async Task<CommandResult> AddUpstreamAsync(string domain, string url, int weight = 1, CancellationToken ct = default)
    {
        return await _client.AddUpstreamAsync(
            new AddUpstreamRequest { Domain = domain, Url = url, Weight = weight },
            cancellationToken: ct);
    }

    public async Task<CommandResult> RemoveUpstreamAsync(string domain, string url, CancellationToken ct = default)
    {
        return await _client.RemoveUpstreamAsync(
            new RemoveUpstreamRequest { Domain = domain, Url = url },
            cancellationToken: ct);
    }
}
