using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Schleusenwerk.Contracts;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Grpc;

internal sealed class RouteServiceImpl : RouteService.RouteServiceBase
{
    private readonly IConfigurationService _config;

    public RouteServiceImpl(IConfigurationService config) => _config = config;

    public override async Task<ListRoutesResponse> ListRoutes(Empty request, ServerCallContext context)
    {
        var result = await _config.GetAllAsync(context.CancellationToken);
        var response = new ListRoutesResponse();

        if (result is ConfigurationResult<ConfigurationSnapshot>.Success success)
        {
            foreach (var domain in success.Value.Domains)
            {
                var upstreams = success.Value.Upstreams.GetValueOrDefault(domain.DomainName.Value, []);
                response.Routes.Add(ProtoMapper.ToRouteSummary(domain, upstreams));
            }
        }

        return response;
    }

    public override async Task<RouteDetail> GetRoute(GetRouteRequest request, ServerCallContext context)
    {
        var domainName = DomainName.Parse(request.Domain);
        var result = await _config.GetByDomainAsync(domainName, context.CancellationToken);

        if (result is ConfigurationResult<DomainConfigResult>.Success success)
        {
            return ProtoMapper.ToRouteDetail(success.Value.Config, success.Value.Upstreams, []);
        }

        throw new RpcException(new Status(StatusCode.NotFound, request.Domain));
    }

    public override async Task<CommandResult> AddRoute(AddRouteRequest request, ServerCallContext context)
    {
        var config = new DomainConfig
        {
            DomainName = DomainName.Parse(request.Domain),
            ForceHttps = request.ForceHttps,
            RequestTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30)
        };

        var addResult = await _config.AddDomainAsync(config, context.CancellationToken);
        if (!addResult.IsSuccess)
        {
            return ProtoMapper.Fail(((ConfigurationResult.Failure)addResult).Error);
        }

        if (!string.IsNullOrWhiteSpace(request.FirstUpstreamUrl))
        {
            var upstream = UpstreamTarget.Create(request.FirstUpstreamUrl);
            var upstreamResult = await _config.AddUpstreamAsync(config.DomainName, upstream, context.CancellationToken);
            if (!upstreamResult.IsSuccess)
            {
                return ProtoMapper.Fail(((ConfigurationResult.Failure)upstreamResult).Error);
            }
        }

        return ProtoMapper.Ok();
    }

    public override async Task<CommandResult> UpdateRoute(UpdateRouteRequest request, ServerCallContext context)
    {
        var getResult = await _config.GetByDomainAsync(DomainName.Parse(request.Domain), context.CancellationToken);
        if (getResult is not ConfigurationResult<DomainConfigResult>.Success existing)
        {
            return ProtoMapper.Fail("Domain not found");
        }

        var updated = existing.Value.Config with
        {
            ForceHttps = request.ForceHttps,
            RequestTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 30)
        };

        var result = await _config.UpdateDomainAsync(updated, context.CancellationToken);
        return result.IsSuccess ? ProtoMapper.Ok() : ProtoMapper.Fail(((ConfigurationResult.Failure)result).Error);
    }

    public override async Task<CommandResult> DeleteRoute(DeleteRouteRequest request, ServerCallContext context)
    {
        var result = await _config.RemoveDomainAsync(DomainName.Parse(request.Domain), context.CancellationToken);
        return result.IsSuccess ? ProtoMapper.Ok() : ProtoMapper.Fail(((ConfigurationResult.Failure)result).Error);
    }

    public override async Task<CommandResult> AddUpstream(AddUpstreamRequest request, ServerCallContext context)
    {
        var upstream = UpstreamTarget.Create(request.Url, request.Weight > 0 ? request.Weight : 1);
        var result = await _config.AddUpstreamAsync(DomainName.Parse(request.Domain), upstream, context.CancellationToken);
        return result.IsSuccess ? ProtoMapper.Ok() : ProtoMapper.Fail(((ConfigurationResult.Failure)result).Error);
    }

    public override async Task<CommandResult> RemoveUpstream(RemoveUpstreamRequest request, ServerCallContext context)
    {
        var result = await _config.RemoveUpstreamAsync(
            DomainName.Parse(request.Domain),
            UpstreamUrl.Parse(request.Url),
            context.CancellationToken);
        return result.IsSuccess ? ProtoMapper.Ok() : ProtoMapper.Fail(((ConfigurationResult.Failure)result).Error);
    }
}
