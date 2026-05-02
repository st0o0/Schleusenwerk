using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;

namespace Schleusenwerk.Routing;

public sealed class UpstreamEntityActor : ReceivePersistentActor
{
    public override string PersistenceId => $"upstream-{Self.Path.Name}";

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IHealthCheckPropsFactory _healthCheckPropsFactory;

    private UpstreamTarget? _target;
    private IActorRef? _healthCheckActor;

    public UpstreamEntityActor(IHealthCheckPropsFactory healthCheckPropsFactory)
    {
        _healthCheckPropsFactory = healthCheckPropsFactory;

        Recover<UpstreamConfigured>(evt => _target = evt.Target);
        Recover<SnapshotOffer>(_ => { });

        Command<RegisterUpstream>(HandleRegisterUpstream);
        Command<SelectUpstreamForDomain>(msg =>
        {
            if (_target is null)
            {
                Sender.Tell(new UpstreamNotFound(msg.Config.DomainName.Value));
                return;
            }
            Sender.Tell(new UpstreamResolved(_target, msg.Config));
        });
    }

    protected override void PreStart()
    {
        base.PreStart();
        StartHealthCheck();
    }

    private void HandleRegisterUpstream(RegisterUpstream msg)
    {
        var evt = new UpstreamConfigured(msg.Target);
        Persist(evt, persisted =>
        {
            _target = persisted.Target;
            StartHealthCheck();
            _log.Info("Upstream configured: {Url}", _target.Url);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void StartHealthCheck()
    {
        if (_healthCheckActor is not null || _target is null)
        {
            return;
        }
        _healthCheckActor = Context.ActorOf(
            _healthCheckPropsFactory.CreateProps(_target),
            "health-check");
    }
}
