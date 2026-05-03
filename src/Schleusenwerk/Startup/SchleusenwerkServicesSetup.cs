using Akka.Actor;
using Akka.Streams;
using Schleusenwerk.Certificates;
using Schleusenwerk.Forwarding;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Metrics;
using Schleusenwerk.Persistence;
using Schleusenwerk.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Servus.Core.Application.Startup;
using TurboHTTP;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkServicesSetup : IServiceSetupContainer
{
    public void SetupServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddTurboHttpClient();
        services.AddSingleton<ProxyMetrics>();
        services.AddSingleton<RequestForwardingPipeline>();
        services.AddSingleton<HeaderManipulationFilter>();
        services.AddSingleton<WebSocketTunnel>();
        services.AddSingleton<IProxyDispatcher, ProxyDispatcher>();

        var connectionString = configuration["Akka:Persistence:ConnectionString"]
            ?? "Data Source=/data/schleusenwerk.db";
        services.AddSingleton<IConfigurationStore>(new SqliteConfigurationStore(connectionString));
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        var certsPath = configuration["Certificates:Path"] ?? "/certs";
        services.AddSingleton<ICertificateStore>(new FileCertificateStore(certsPath));
        services.AddSingleton<SniCertificateSelector>();
        services.AddSingleton<AcmeChallengeStore>();
        services.AddSingleton<IAcmeClient>(sp =>
            new CertesAcmeClient(
                sp.GetRequiredService<IConfigurationStore>(),
                certsPath,
                sp.GetRequiredService<ILogger<CertesAcmeClient>>()));

        var urls = configuration["ASPNETCORE_URLS"] ?? configuration["urls"] ?? "";
        if (urls.Contains("https", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<KestrelServerOptions>(options =>
            {
                options.ConfigureHttpsDefaults(adapterOptions =>
                {
                    var selector = options.ApplicationServices!.GetRequiredService<SniCertificateSelector>();
                    adapterOptions.ServerCertificateSelector = (_, hostname) => selector.Select(hostname);
                });
            });
        }

        var rateLimitCache = new RateLimitConfigCache();
        services.AddSingleton(rateLimitCache);
        services.AddRateLimiter(options => options.ConfigurePolicy(rateLimitCache));

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173,http://localhost:3000";
                policy.WithOrigins(allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        services.AddControllers();
        services.AddSignalR();
        services.AddHostedService<Hubs.EventBridgeService>();
        services.AddSingleton<IMaterializer>(sp =>
            sp.GetRequiredService<ActorSystem>().Materializer());
    }
}
