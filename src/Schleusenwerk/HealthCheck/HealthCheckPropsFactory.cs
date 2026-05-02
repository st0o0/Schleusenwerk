using Akka.Actor;
using Schleusenwerk.Routing;

namespace Schleusenwerk.HealthCheck;

public sealed class HealthCheckPropsFactory(IHttpClientFactory httpClientFactory) : IHealthCheckPropsFactory
{
    public Props CreateProps(UpstreamTarget target)
    {
        return Props.Create(() => new HealthCheckActor(target, httpClientFactory));
    }
}
