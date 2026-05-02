using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Sql.Hosting;
using LinqToDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Discovery;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.LoadBalancing;
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

        Func<IReadOnlyList<UpstreamTarget>, Props> loadBalancerPropsFactory =
            upstreams => Props.Create(() => new LoadBalancerActor(upstreams, healthCheckPropsFactory));

        builder.WithActors((system, registry, resolver) =>
        {
            var eventHub = system.ActorOf(resolver.Props<EventHub>(), "eventHub");
            registry.Register<EventHub>(eventHub);

            var domainRouter = system.ActorOf(
                Props.Create(() => new DomainRouterActor(loadBalancerPropsFactory)),
                "domain-router");
            registry.Register<DomainRouterActor>(domainRouter);

            var config = system.ActorOf(resolver.Props<ConfigurationPersistenceActor>(), "configuration");
            registry.Register<ConfigurationPersistenceActor>(config);

            var dockerDiscovery = system.ActorOf(resolver.Props<DockerDiscoveryActor>(), "docker-discovery");
            registry.Register<DockerDiscoveryActor>(dockerDiscovery);
        });
    }
}
