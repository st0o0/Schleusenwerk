using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Certificates;

public sealed class CertificateProvisioningActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ICertificateStore _store;
    private readonly IActorRef _eventHub;

    public CertificateProvisioningActor(ICertificateStore store)
    {
        _store = store;
        _eventHub = Context.GetActor<EventHub>();

        Receive<CertificateProvisioningRequested>(Handle);
        Receive<EventHub.Subscribed>(msg =>
        {
            msg.SourceRef.Source
                .RunWith(
                    Sink.ActorRef<IClusterEvent>(Self, StreamCompleted.Instance, ex => new StreamFailed(ex)),
                    Context.Materializer());
        });
        Receive<StreamCompleted>(_ =>
            _log.Warning("Certificate event stream completed unexpectedly"));
        Receive<StreamFailed>(msg =>
            _log.Error(msg.Ex, "Certificate event stream failed"));
    }

    protected override void PreStart()
    {
        base.PreStart();
        _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe<ICertificateEvent>.Instance)
            .PipeTo(Self);
    }

    private void Handle(CertificateProvisioningRequested msg)
    {
        if (_store.HasCertificate(msg.DomainName))
        {
            _log.Info("Certificate already exists for {Domain}, skipping", msg.DomainName);
            return;
        }

        _log.Info("Generating self-signed certificate for {Domain}", msg.DomainName);
        using var cert = SelfSignedCertificateGenerator.Generate(msg.DomainName);
        _store.StoreCertificate(msg.DomainName, cert);
        _log.Info("Stored self-signed certificate for {Domain}", msg.DomainName);
    }

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Ex);
}
