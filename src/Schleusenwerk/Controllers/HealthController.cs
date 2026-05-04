using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly IConfigurationStore _store;
    private readonly IActorRef _domainRegion;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public HealthController(IConfigurationStore store, IReadOnlyActorRegistry registry)
    {
        _store = store;
        _domainRegion = registry.Get<DomainEntityActor>();
    }

    [HttpGet]
    public async Task<ActionResult<ProxyHealthResponseDto>> GetHealth(CancellationToken ct)
    {
        var domains = await _store.GetAllDomainsAsync(ct);
        var healthTasks = domains.Select(d => GetDomainHealth(d.DomainName, ct));
        var results = await Task.WhenAll(healthTasks);

        var healthyCount = 0;
        var unhealthyCount = 0;
        foreach (var entries in results)
        {
            if (entries.Any(e => e.IsHealthy))
            {
                healthyCount++;
            }
            else
            {
                unhealthyCount++;
            }
        }

        return Ok(new ProxyHealthResponseDto(domains.Count, healthyCount, unhealthyCount));
    }

    [HttpGet("{domain}")]
    public async Task<ActionResult<UpstreamHealthResponseDto>> GetUpstreamHealth(string domain, CancellationToken ct)
    {
        var domainName = DomainName.Parse(domain);
        var entries = await GetDomainHealth(domainName, ct);
        var upstreams = entries
            .Select(e => new UpstreamHealthEntryDto(e.Url.Value.ToString(), e.IsHealthy))
            .ToList();
        return Ok(new UpstreamHealthResponseDto(domain, upstreams));
    }

    private async Task<IReadOnlyList<UpstreamHealthStatus>> GetDomainHealth(DomainName domain, CancellationToken ct)
    {
        var result = await _domainRegion.Ask<DomainUpstreamHealthResult>(
            new GetDomainUpstreamHealth { Domain = domain.Value }, _timeout, ct);
        return result.Entries;
    }
}
