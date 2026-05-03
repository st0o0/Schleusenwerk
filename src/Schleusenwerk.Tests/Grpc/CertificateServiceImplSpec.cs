using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Schleusenwerk.Certificates;
using Schleusenwerk.Contracts;
using Schleusenwerk.Grpc;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Grpc;

public sealed class CertificateServiceImplSpec : PersistenceTestKit
{
    private readonly FakeCertificateStore _store = new();

    private CertificateServiceImpl CreateSut()
    {
        var registry = ActorRegistry.For(Sys);
        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-cert-{Guid.NewGuid():N}");
        registry.Register<EventHub>(hub, overwrite: true);
        return new CertificateServiceImpl(_store, registry);
    }

    [Fact(Timeout = 5000)]
    public async Task ListCertificates_should_return_empty_when_no_certs()
    {
        var response = await CreateSut().ListCertificates(new Empty(), FakeServerCallContext.Instance);

        Assert.Empty(response.Certificates);
    }

    [Fact(Timeout = 5000)]
    public async Task ListCertificates_should_return_all_stored_certs()
    {
        var domain = DomainName.Parse("example.com");
        var cert = SelfSignedCertificateGenerator.Generate(domain);
        _store.Seed(domain, cert);

        var response = await CreateSut().ListCertificates(new Empty(), FakeServerCallContext.Instance);

        Assert.Single(response.Certificates);
        Assert.Equal("example.com", response.Certificates[0].Domain);
        Assert.NotEmpty(response.Certificates[0].Thumbprint);
    }

    [Fact(Timeout = 5000)]
    public async Task GetCertificate_should_throw_not_found_for_unknown_domain()
    {
        var request = new GetCertificateRequest { Domain = "unknown.example.com" };

        await Assert.ThrowsAsync<RpcException>(
            () => CreateSut().GetCertificate(request, FakeServerCallContext.Instance));
    }

    [Fact(Timeout = 5000)]
    public async Task ProvisionCertificate_should_return_success()
    {
        var request = new ProvisionCertificateRequest { Domain = "example.com" };

        var result = await CreateSut().ProvisionCertificate(request, FakeServerCallContext.Instance);

        Assert.True(result.Success);
    }
}
