using Akka.Actor;
using Akka.Event;

namespace Schleusenwerk.Routing;

public sealed class UpstreamEntityActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Func<UpstreamTarget, Props> _healthCheckPropsFactory;

    private UpstreamTarget? _target;
    private IActorRef? _healthCheckActor;

    public UpstreamEntityActor(Func<UpstreamTarget, Props> healthCheckPropsFactory)
    {
        _healthCheckPropsFactory = healthCheckPropsFactory;

        Receive<RegisterUpstream>(HandleRegisterUpstream);
        Receive<SelectUpstreamForDomain>(msg =>
        {
            if (_target is null)
            {
                Sender.Tell(new UpstreamNotFound(msg.Config.DomainName.Value));
                return;
            }
            Sender.Tell(new UpstreamResolved(_target, msg.Config));
        });
    }

    private void HandleRegisterUpstream(RegisterUpstream msg)
    {
        _target = msg.Target;

        if (_healthCheckActor == null)
        {
            _healthCheckActor = Context.ActorOf(
                _healthCheckPropsFactory(_target),
                "health-check");
            _healthCheckActor.Tell(msg);
        }
    }
}
