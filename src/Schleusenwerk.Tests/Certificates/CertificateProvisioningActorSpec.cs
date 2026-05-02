using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.Certificates;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class CertificateProvisioningActorSpec : PersistenceTestKit
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prov-test-{Guid.NewGuid():N}");
    private int _actorCounter;

    private (IActorRef actor, FileCertificateStore store) CreateActor()
    {
        var id = Interlocked.Increment(ref _actorCounter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-prov-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var store = new FileCertificateStore(_tempDir);
        var actor = Sys.ActorOf(
            Props.Create(() => new CertificateProvisioningActor(store)),
            $"cert-prov-{id}");

        return (actor, store);
    }

    protected override void AfterAll()
    {
        base.AfterAll();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task CertificateProvisioningActor_should_generate_cert_on_request()
    {
        var (actor, store) = CreateActor();
        var domain = DomainName.Parse("example.com");

        actor.Tell(new CertificateProvisioningRequested(domain));

        await Task.Delay(500);

        Assert.True(store.HasCertificate(domain));
        using var cert = store.GetCertificate(domain);
        Assert.NotNull(cert);
        Assert.Contains("CN=example.com", cert.Subject);
    }

    [Fact(Timeout = 5000)]
    public async Task CertificateProvisioningActor_should_skip_if_cert_already_exists()
    {
        var (actor, store) = CreateActor();
        var domain = DomainName.Parse("already-exists.com");

        using var existingCert = SelfSignedCertificateGenerator.Generate(domain);
        store.StoreCertificate(domain, existingCert);
        var originalThumbprint = existingCert.Thumbprint;

        actor.Tell(new CertificateProvisioningRequested(domain));

        await Task.Delay(500);

        using var loaded = store.GetCertificate(domain);
        Assert.Equal(originalThumbprint, loaded!.Thumbprint);
    }
}
