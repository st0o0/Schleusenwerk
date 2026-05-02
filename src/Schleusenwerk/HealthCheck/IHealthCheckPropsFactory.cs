using Akka.Actor;
using Schleusenwerk.Routing;

namespace Schleusenwerk.HealthCheck;

public interface IHealthCheckPropsFactory
{
    Props CreateProps(UpstreamTarget target);
}
