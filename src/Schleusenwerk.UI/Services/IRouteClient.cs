using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

public interface IRouteClient
{
    Task<IReadOnlyList<RouteSummary>> ListRoutesAsync(CancellationToken ct = default);
    Task<RouteDetail> GetRouteAsync(string domain, CancellationToken ct = default);
    Task<CommandResult> AddRouteAsync(AddRouteRequest request, CancellationToken ct = default);
    Task<CommandResult> UpdateRouteAsync(UpdateRouteRequest request, CancellationToken ct = default);
    Task<CommandResult> DeleteRouteAsync(string domain, CancellationToken ct = default);
    Task<CommandResult> AddUpstreamAsync(string domain, string url, int weight = 1, CancellationToken ct = default);
    Task<CommandResult> RemoveUpstreamAsync(string domain, string url, CancellationToken ct = default);
}
