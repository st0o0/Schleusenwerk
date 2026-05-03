using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Persistence.Sql.Hosting;
using Akka.Remote.Hosting;
using LinqToDB;
using Schleusenwerk.Certificates;
using Schleusenwerk.Discovery;
using Schleusenwerk.HealthCheck;
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
        var hostname = configuration["Akka:Remoting:Hostname"] ?? "127.0.0.1";
        var port = int.TryParse(configuration["Akka:Remoting:Port"], out var p) ? p : 2552;

        builder.WithSqlPersistence(connectionString, ProviderName.SQLiteMS);

        builder.WithRemoting(hostname, port);
        builder.WithClustering(new ClusterOptions
        {
            Roles = ["schleusenwerk"],
            SeedNodes = [$"akka.tcp://schleusenwerk@{hostname}:{port}"]
        });

        var messageExtractor = HashCodeMessageExtractor.Create(
            maxNumberOfShards: 20,
            entityIdExtractor: msg => (msg as IWithEntityId)?.EntityId);

        var configStore = serviceProvider.GetRequiredService<IConfigurationStore>();

        builder.WithShardRegion<DomainEntityActor>(
            "domain-router",
            entityId => Props.Create(() => new DomainEntityActor(configStore)),
            messageExtractor,
            new ShardOptions
            {
                PassivateIdleEntityAfter = TimeSpan.FromMinutes(5),
                RememberEntities = true
            });

        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        builder.WithShardRegion<HealthCheckEntityActor>(
            "health-check",
            entityId =>
            {
                var url = UpstreamUrl.Parse(Uri.UnescapeDataString(entityId));
                var target = new UpstreamTarget { Url = url };
                return Props.Create(() => new HealthCheckEntityActor(target, httpClientFactory));
            },
            messageExtractor,
            new ShardOptions
            {
                PassivateIdleEntityAfter = TimeSpan.FromMinutes(10),
                RememberEntities = false
            });

        builder.WithActors((system, registry, resolver) =>
        {
            var eventHub = system.ActorOf(resolver.Props<EventHub>(), "eventHub");
            registry.Register<EventHub>(eventHub);

            var dockerDiscovery = system.ActorOf(resolver.Props<DockerDiscoveryActor>(), "docker-discovery");
            registry.Register<DockerDiscoveryActor>(dockerDiscovery);

            var certProvisioning = system.ActorOf(
                Props.Create(() => new CertificateProvisioningActor(
                    serviceProvider.GetRequiredService<ICertificateStore>(),
                    serviceProvider.GetRequiredService<IConfigurationStore>(),
                    serviceProvider.GetRequiredService<IAcmeClient>(),
                    serviceProvider.GetRequiredService<AcmeChallengeStore>())),
                "cert-provisioning");
            registry.Register<CertificateProvisioningActor>(certProvisioning);
        });
    }
}
