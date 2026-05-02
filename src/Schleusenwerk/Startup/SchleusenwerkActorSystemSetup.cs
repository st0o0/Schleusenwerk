using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Persistence.Sql.Hosting;
using Akka.Remote.Hosting;
using LinqToDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Discovery;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka.Startup;
using Akka.Util;

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

        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        Func<UpstreamTarget, Props> healthCheckPropsFactory = upstream =>
        {
            var config = new HealthCheckConfig();
            Func<UpstreamUrl, string, TimeSpan, CancellationToken, Task<bool>> probeFunc =
                async (url, endpoint, timeout, ct) =>
                {
                    using var client = httpClientFactory.CreateClient("health-check");
                    client.Timeout = timeout;
                    try
                    {
                        var uri = new Uri($"{url}{endpoint.TrimStart('/')}");
                        using var response = await client.GetAsync(uri, ct);
                        return response.IsSuccessStatusCode;
                    }
                    catch
                    {
                        return false;
                    }
                };

            return Props.Create(() => new HealthCheckActor(upstream.Url, config, probeFunc));
        };

        // Register upstream region first — DomainEntityActor props factory reads it from registry
        builder.WithShardRegion<UpstreamEntityActor>(
            "upstream-pool",
            entityId => Props.Create(() => new UpstreamEntityActor(healthCheckPropsFactory)),
            msg => msg is IWithUrl m
                ? Option<(string, object)>.Create((m.Url, msg))
                : Option<(string, object)>.None,
            msg => msg is IWithUrl m
                ? Math.Abs(m.Url.GetHashCode() % 20).ToString()
                : null!,
            new ShardOptions { PassivateIdleEntityAfter = TimeSpan.FromMinutes(5) });

        // Register domain region using WithActors to get access to the registry after upstream is registered
        builder.WithActors((system, registry, resolver) =>
        {
            var domainShardRegion = ClusterSharding.Get(system).Start(
                typeName: "domain-router",
                entityProps: Props.Create(() => new DomainEntityActor(registry.Get<UpstreamEntityActor>())),
                settings: ClusterShardingSettings.Create(system),
                extractEntityId: (object msg) => msg is IWithDomain m
                    ? (m.Domain, msg)
                    : default,
                extractShardId: (object msg) => msg is IWithDomain m
                    ? Math.Abs(m.Domain.GetHashCode() % 20).ToString()
                    : null!);

            registry.Register<DomainEntityActor>(domainShardRegion);

            var eventHub = system.ActorOf(resolver.Props<EventHub>(), "eventHub");
            registry.Register<EventHub>(eventHub);

            var config = system.ActorOf(resolver.Props<ConfigurationPersistenceActor>(), "configuration");
            registry.Register<ConfigurationPersistenceActor>(config);

            var dockerDiscovery = system.ActorOf(resolver.Props<DockerDiscoveryActor>(), "docker-discovery");
            registry.Register<DockerDiscoveryActor>(dockerDiscovery);
        });
    }
}
