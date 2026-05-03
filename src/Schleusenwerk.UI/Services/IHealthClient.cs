using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Services;

public interface IHealthClient
{
    Task<ProxyHealthResponse> GetHealthAsync(CancellationToken ct = default);
    Task<UpstreamHealthResponse> GetUpstreamHealthAsync(string domain, CancellationToken ct = default);
}
