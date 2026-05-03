using Akka.Actor;
using Akka.Hosting;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Schleusenwerk.Certificates;
using Schleusenwerk.Contracts;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Grpc;

internal sealed class CertificateServiceImpl : CertificateService.CertificateServiceBase
{
    private readonly ICertificateStore _store;
    private readonly IReadOnlyActorRegistry _registry;

    public CertificateServiceImpl(ICertificateStore store, IReadOnlyActorRegistry registry)
    {
        _store = store;
        _registry = registry;
    }

    public override Task<ListCertificatesResponse> ListCertificates(Empty request, ServerCallContext context)
    {
        var response = new ListCertificatesResponse();
        foreach (var domain in _store.ListDomains())
        {
            var cert = _store.GetCertificate(domain);
            if (cert is not null)
            {
                response.Certificates.Add(ProtoMapper.ToCertificateSummary(domain, cert));
            }
        }
        return Task.FromResult(response);
    }

    public override Task<CertificateDetail> GetCertificate(GetCertificateRequest request, ServerCallContext context)
    {
        var domain = DomainName.Parse(request.Domain);
        var cert = _store.GetCertificate(domain);

        if (cert is null)
        {
            throw new RpcException(new global::Grpc.Core.Status(StatusCode.NotFound, request.Domain));
        }

        return Task.FromResult(ProtoMapper.ToCertificateDetail(domain, cert));
    }

    public override Task<CommandResult> ProvisionCertificate(ProvisionCertificateRequest request, ServerCallContext context)
    {
        var eventHub = _registry.Get<EventHub>();
        eventHub.Tell(new Persistence.CertificateProvisioningRequested(DomainName.Parse(request.Domain)));
        return Task.FromResult(ProtoMapper.Ok());
    }
}
