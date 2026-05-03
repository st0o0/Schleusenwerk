using Akka.Actor;
using Akka.Hosting;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Schleusenwerk.Contracts;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Grpc;

internal sealed class HealthServiceImpl : HealthService.HealthServiceBase
{
    private readonly IConfigurationStore _store;
    private readonly IActorRef _domainRegion;
    private readonly TimeSpan _timeout;

    public HealthServiceImpl(IConfigurationStore store, IReadOnlyActorRegistry registry, TimeSpan? timeout = null)
    {
        _store = store;
        _domainRegion = registry.Get<DomainEntityActor>();
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public override async Task<ProxyHealthResponse> GetHealth(Empty request, ServerCallContext context)
    {
        var domains = await _store.GetAllDomainsAsync(context.CancellationToken);
        var response = new ProxyHealthResponse { RouteCount = domains.Count };

        var healthTasks = domains.Select(d => GetDomainHealth(d.DomainName, context.CancellationToken));
        var results = await Task.WhenAll(healthTasks);

        foreach (var entries in results)
        {
            var hasHealthy = entries.Any(e => e.IsHealthy);
            if (hasHealthy)
            {
                response.HealthyCount++;
            }
            else
            {
                response.UnhealthyCount++;
            }
        }

        return response;
    }

    public override async Task<UpstreamHealthResponse> GetUpstreamHealth(
        GetUpstreamHealthRequest request, ServerCallContext context)
    {
        var domain = DomainName.Parse(request.Domain);
        var entries = await GetDomainHealth(domain, context.CancellationToken);
        var response = new UpstreamHealthResponse { Domain = request.Domain };
        response.Upstreams.AddRange(entries.Select(e => new UpstreamHealthEntry
        {
            Url = e.Url.Value.ToString(),
            IsHealthy = e.IsHealthy
        }));
        return response;
    }

    private async Task<IReadOnlyList<UpstreamHealthStatus>> GetDomainHealth(
        DomainName domain, CancellationToken ct)
    {
        var result = await _domainRegion.Ask<DomainUpstreamHealthResult>(
            new GetDomainUpstreamHealth { Domain = domain.Value }, _timeout, ct);
        return result.Entries;
    }
}
