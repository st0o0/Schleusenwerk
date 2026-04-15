using Akka.Cluster.Hosting;
using Akka.Hosting;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkActorSystemSetup : ActorSystemSetupContainer
{
    protected override string GetActorSystemName() => "schleusenwerk";

    protected override void BuildSystem(AkkaConfigurationBuilder builder, IServiceProvider serviceProvider)
    {
        builder.WithSingleton<EventHub>("eventHub", "eventHub",
            (_, _, resolver) => resolver.Props<EventHub>(), new ClusterSingletonOptions { Role = "proxy", });

        builder.WithActors((system, registry, resolver) =>
        {
            var domainRouter = system.ActorOf(resolver.Props<DomainRouterActor>(), "domain-router");
            registry.Register<DomainRouterActor>(domainRouter);

            var configuration = system.ActorOf(resolver.Props<ConfigurationPersistenceActor>(), "configuration");
            registry.Register<ConfigurationPersistenceActor>(configuration);
        });
    }
}