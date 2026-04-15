namespace Schleusenwerk.Domain.LoadBalancing;

using Akka.Actor;
using Schleusenwerk.Domain.Routing;

/// <summary>
/// Lightweight actor representing a single upstream slot in the round-robin rotation.
/// Multiple instances per upstream are created to reflect its weight.
/// </summary>
internal sealed class UpstreamRouteeActor : ReceiveActor
{
    public UpstreamRouteeActor(UpstreamTarget target)
    {
        Receive<SelectUpstream>(_ => Sender.Tell(new UpstreamSelected(target)));
    }

    public static Props CreateProps(UpstreamTarget target) =>
        Props.Create(() => new UpstreamRouteeActor(target));
}
