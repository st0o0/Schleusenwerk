using Akka.Actor;
using Schleusenwerk.Routing;

namespace Schleusenwerk.LoadBalancing;

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
}