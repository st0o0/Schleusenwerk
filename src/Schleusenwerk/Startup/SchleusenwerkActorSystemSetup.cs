using Akka.Hosting;
using Akka.Persistence.Sql.Hosting;
using LinqToDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkActorSystemSetup : ActorSystemSetupContainer
{
    protected override string GetActorSystemName() => "schleusenwerk";

    protected override void BuildSystem(AkkaConfigurationBuilder builder, IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var connectionString = configuration["Akka:Persistence:ConnectionString"] ?? "Data Source=/data/schleusenwerk.db";

        builder.WithSqlPersistence(connectionString, ProviderName.SQLiteMS);

        builder.WithActors((system, registry, resolver) =>
        {
            var eventHub = system.ActorOf(resolver.Props<EventHub>(), "eventHub");
            registry.Register<EventHub>(eventHub);

            var domainRouter = system.ActorOf(resolver.Props<DomainRouterActor>(), "domain-router");
            registry.Register<DomainRouterActor>(domainRouter);

            var config = system.ActorOf(resolver.Props<ConfigurationPersistenceActor>(), "configuration");
            registry.Register<ConfigurationPersistenceActor>(config);
        });
    }
}
