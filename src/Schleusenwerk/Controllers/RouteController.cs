using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/routes")]
public sealed class RouteController : ControllerBase
{
    private readonly IConfigurationService _config;

    public RouteController(IConfigurationService config) => _config = config;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RouteSummaryDto>>> ListRoutes(CancellationToken ct)
    {
        var result = await _config.GetAllAsync(ct);
        if (result is not ConfigurationResult<ConfigurationSnapshot>.Success success)
        {
            return Ok(Array.Empty<RouteSummaryDto>());
        }

        var routes = success.Value.Domains.Select(domain =>
        {
            var upstreams = success.Value.Upstreams.GetValueOrDefault(domain.DomainName.Value, []);
            return DomainModelMapper.ToRouteSummary(domain, upstreams);
        }).ToList();

        return Ok(routes);
    }

    [HttpGet("{domain}")]
    public async Task<ActionResult<RouteDetailDto>> GetRoute(string domain, CancellationToken ct)
    {
        var domainName = DomainName.Parse(domain);
        var result = await _config.GetByDomainAsync(domainName, ct);

        if (result is not ConfigurationResult<DomainConfigResult>.Success success)
        {
            return NotFound();
        }

        return Ok(DomainModelMapper.ToRouteDetail(success.Value.Config, success.Value.Upstreams, []));
    }

    [HttpPost]
    public async Task<ActionResult<CommandResultDto>> AddRoute([FromBody] AddRouteRequestDto request, CancellationToken ct)
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse(request.Domain),
            ForceHttps = request.ForceHttps,
            RequestTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30)
        };

        var addResult = await _config.AddDomainAsync(config, ct);
        if (!addResult.IsSuccess)
        {
            return Ok(CommandResultDto.Fail(((ConfigurationResult.Failure)addResult).Error));
        }

        if (!string.IsNullOrWhiteSpace(request.FirstUpstreamUrl))
        {
            var upstream = UpstreamTarget.Create(request.FirstUpstreamUrl);
            var upstreamResult = await _config.AddUpstreamAsync(config.DomainName, upstream, ct);
            if (!upstreamResult.IsSuccess)
            {
                return Ok(CommandResultDto.Fail(((ConfigurationResult.Failure)upstreamResult).Error));
            }
        }

        return Ok(CommandResultDto.Ok());
    }

    [HttpPut("{domain}")]
    public async Task<ActionResult<CommandResultDto>> UpdateRoute(
        string domain, [FromBody] UpdateRouteRequestDto request, CancellationToken ct)
    {
        var getResult = await _config.GetByDomainAsync(DomainName.Parse(domain), ct);
        if (getResult is not ConfigurationResult<DomainConfigResult>.Success existing)
        {
            return Ok(CommandResultDto.Fail("Domain not found"));
        }

        var updated = existing.Value.Config with
        {
            ForceHttps = request.ForceHttps,
            RequestTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30)
        };

        var result = await _config.UpdateDomainAsync(updated, ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }

    [HttpDelete("{domain}")]
    public async Task<ActionResult<CommandResultDto>> DeleteRoute(string domain, CancellationToken ct)
    {
        var result = await _config.RemoveDomainAsync(DomainName.Parse(domain), ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }

    [HttpPost("{domain}/upstreams")]
    public async Task<ActionResult<CommandResultDto>> AddUpstream(
        string domain, [FromBody] AddUpstreamRequestDto request, CancellationToken ct)
    {
        var upstream = UpstreamTarget.Create(request.Url, request.Weight > 0 ? request.Weight : 1);
        var result = await _config.AddUpstreamAsync(DomainName.Parse(domain), upstream, ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }

    [HttpDelete("{domain}/upstreams/{encodedUrl}")]
    public async Task<ActionResult<CommandResultDto>> RemoveUpstream(
        string domain, string encodedUrl, CancellationToken ct)
    {
        var urlBytes = Convert.FromBase64String(encodedUrl.Replace('-', '+').Replace('_', '/'));
        var url = System.Text.Encoding.UTF8.GetString(urlBytes);

        var result = await _config.RemoveUpstreamAsync(
            DomainName.Parse(domain),
            UpstreamUrl.Parse(url),
            ct);
        return Ok(result.IsSuccess
            ? CommandResultDto.Ok()
            : CommandResultDto.Fail(((ConfigurationResult.Failure)result).Error));
    }
}
