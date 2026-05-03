using Akka.Actor;
using Schleusenwerk.Routing;

namespace Schleusenwerk.HealthCheck;

public sealed record SubscribeHealth(IActorRef Subscriber) : IWithUrl
{
    public required string Url { get; init; }
}

public sealed record UnsubscribeHealth(IActorRef Subscriber) : IWithUrl
{
    public required string Url { get; init; }
}
