using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;

namespace Schleusenwerk.Certificates;

public sealed class CertificateProvisioningActor : ReceiveActor, IWithTimers
{
    private static readonly TimeSpan RenewalCheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RenewalThreshold = TimeSpan.FromDays(30);
    private static readonly TimeSpan WarningThreshold = TimeSpan.FromDays(14);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ICertificateStore _store;
    private readonly IConfigurationStore _configStore;
    private readonly IAcmeClient _acmeClient;
    private readonly AcmeChallengeStore _challengeStore;
    private readonly IActorRef _eventHub;

    public CertificateProvisioningActor(
        ICertificateStore store,
        IConfigurationStore configStore,
        IAcmeClient acmeClient,
        AcmeChallengeStore challengeStore)
    {
        _store = store;
        _configStore = configStore;
        _acmeClient = acmeClient;
        _challengeStore = challengeStore;
        _eventHub = Context.GetActor<EventHub>();

        Receive<CertificateProvisioningRequested>(Handle);
        Receive<CheckRenewals>(_ => HandleCheckRenewals());
        Receive<ProvisioningResult>(Handle);
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

        Timers.StartPeriodicTimer("renewal-check", CheckRenewals.Instance, RenewalCheckInterval, RenewalCheckInterval);
    }

    private void Handle(CertificateProvisioningRequested msg)
    {
        ProvisionAsync(msg.DomainName, 0);
    }

    private void ProvisionAsync(DomainName domain, int attempt)
    {
        var self = Self;

        Task.Run(async () =>
        {
            try
            {
                var settings = await _configStore.GetSettingsAsync();

                if (settings.Stage == AcmeStage.Local)
                {
                    if (_store.HasCertificate(domain))
                    {
                        return new ProvisioningResult(domain, true, null, attempt);
                    }

                    using var cert = SelfSignedCertificateGenerator.Generate(domain);
                    _store.StoreCertificate(domain, cert);
                    return new ProvisioningResult(domain, true, null, attempt);
                }

                var order = await _acmeClient.StartOrderAsync(domain);
                _challengeStore.SetChallenge(order.Token, order.KeyAuthorization);

                try
                {
                    using var cert = await _acmeClient.CompleteOrderAsync(domain);
                    _store.StoreCertificate(domain, cert);
                    return new ProvisioningResult(domain, true, null, attempt);
                }
                finally
                {
                    _challengeStore.RemoveChallenge(order.Token);
                }
            }
            catch (Exception ex)
            {
                return new ProvisioningResult(domain, false, ex.Message, attempt);
            }
        }).PipeTo(self);
    }

    private void Handle(ProvisioningResult msg)
    {
        if (msg.Success)
        {
            _log.Info("Certificate provisioned for {Domain}", msg.Domain);
        }
        else
        {
            _log.Warning("Certificate provisioning failed for {Domain}: {Error}", msg.Domain, msg.Error);

            var delay = TimeSpan.FromMinutes(Math.Min(Math.Pow(2, msg.Attempt), MaxRetryDelay.TotalMinutes));
            _log.Info("Retrying provisioning for {Domain} in {Delay}", msg.Domain, delay);
            Timers.StartSingleTimer(
                $"retry-{msg.Domain.Value}",
                new CertificateProvisioningRequested(msg.Domain),
                delay);
        }
    }

    private void HandleCheckRenewals()
    {
        foreach (var domain in _store.ListDomains())
        {
            var cert = _store.GetCertificate(domain);
            if (cert is null)
            {
                continue;
            }

            var remaining = cert.NotAfter - DateTime.UtcNow;

            if (remaining < WarningThreshold)
            {
                _eventHub.Tell(new CertificateExpiring(domain));
            }

            if (remaining < RenewalThreshold)
            {
                _log.Info("Certificate for {Domain} expires in {Days} days, triggering renewal",
                    domain, (int)remaining.TotalDays);
                Self.Tell(new CertificateProvisioningRequested(domain));
            }
        }
    }

    private sealed record CheckRenewals
    {
        public static readonly CheckRenewals Instance = new();
    }

    private sealed record ProvisioningResult(DomainName Domain, bool Success, string? Error, int Attempt);

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Ex);
}
