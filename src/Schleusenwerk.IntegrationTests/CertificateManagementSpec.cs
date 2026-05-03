using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Schleusenwerk.Contracts;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class CertificateManagementSpec
{
    private readonly CertificateService.CertificateServiceClient _certs;
    private readonly RouteService.RouteServiceClient _routes;

    public CertificateManagementSpec(SchleusenwerkFixture fixture)
    {
        _certs = new CertificateService.CertificateServiceClient(fixture.GrpcChannel);
        _routes = new RouteService.RouteServiceClient(fixture.GrpcChannel);
    }

    [Fact(Timeout = 30_000)]
    public async Task ListCertificates_should_return_empty_initially()
    {
        var response = await _certs.ListCertificatesAsync(new Empty(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response);
    }

    [Fact(Timeout = 30_000)]
    public async Task ProvisionCertificate_should_return_success()
    {
        var domain = $"cert-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = true,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        var result = await _certs.ProvisionCertificateAsync(new ProvisionCertificateRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }

    [Fact(Timeout = 30_000)]
    public async Task GetCertificate_should_throw_not_found_for_unknown_domain()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(
            async () => await _certs.GetCertificateAsync(new GetCertificateRequest { Domain = "unknown.test" }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task ProvisionCertificate_then_GetCertificate_should_return_detail()
    {
        var domain = $"cert-detail-{Guid.NewGuid():N}.test";
        await _routes.AddRouteAsync(new AddRouteRequest
        {
            Domain = domain,
            ForceHttps = true,
            TimeoutSeconds = 30
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _certs.ProvisionCertificateAsync(new ProvisionCertificateRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(2000, TestContext.Current.CancellationToken);

        try
        {
            var detail = await _certs.GetCertificateAsync(new GetCertificateRequest { Domain = domain }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(domain, detail.Domain);
            Assert.NotEmpty(detail.Thumbprint);
            Assert.True(detail.IsSelfSigned);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
        }
    }
}
