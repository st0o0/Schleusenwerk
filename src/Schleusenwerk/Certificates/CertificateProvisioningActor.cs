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
    private readonly IConfigurationService _configService;
    private readonly ILegoCertificateProvider _lego;
    private readonly IActorRef _eventHub;

    public CertificateProvisioningActor(
        ICertificateStore store,
        IConfigurationStore configStore,
        IConfigurationService configService,
        ILegoCertificateProvider lego)
    {
        _store = store;
        _configStore = configStore;
        _configService = configService;
        _lego = lego;
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
                var domainResult = await _configService.GetByDomainAsync(domain);
                var tlsMode = domainResult is ConfigurationResult<DomainConfigResult>.Success success
                    ? success.Value.Config.TlsMode
                    : TlsMode.LetsEncrypt;

                if (settings.Stage == AcmeStage.Local || tlsMode == TlsMode.SelfSigned)
                {
                    if (_store.HasCertificate(domain))
                    {
                        return new ProvisioningResult(domain, true, null, attempt);
                    }

                    using var cert = SelfSignedCertificateGenerator.Generate(domain);
                    _store.StoreCertificate(domain, cert);
                    return new ProvisioningResult(domain, true, null, attempt);
                }

                if (tlsMode == TlsMode.Custom)
                {
                    return new ProvisioningResult(domain, true, null, attempt);
                }

                if (tlsMode == TlsMode.Dns && string.IsNullOrWhiteSpace(settings.DnsProvider))
                {
                    return new ProvisioningResult(domain, false, "DNS-01 requested but no LEGO_DNS_PROVIDER configured", attempt);
                }

                using var legoCert = await _lego.ProvisionAsync(domain, tlsMode);
                _store.StoreCertificate(domain, legoCert);
                return new ProvisioningResult(domain, true, null, attempt);
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
