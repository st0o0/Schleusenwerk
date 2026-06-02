using Akka.Actor;
using Akka.Streams;
using Schleusenwerk.Certificates;
using Schleusenwerk.Forwarding;
using Schleusenwerk.Metrics;
using Schleusenwerk.Persistence;
using Schleusenwerk.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Servus.Core.Application.Startup;
namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkServicesSetup : IServiceSetupContainer
{
    public void SetupServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddHttpClient("upstream")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                EnableMultipleHttp2Connections = true,
            });
        services.AddSingleton<ConnectionTracker>();
        services.AddSingleton<AccessLogMiddleware>();
        services.AddHostedService<GracefulShutdownService>();
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

        var legoPath = configuration["Lego:Path"] ?? "/certs/lego";
        var webrootPath = configuration["Lego:WebrootPath"] ?? "/tmp/acme-webroot";
        services.AddSingleton<ILegoCertificateProvider>(sp =>
            new LegoCertificateProvider(
                sp.GetRequiredService<IConfigurationStore>(),
                legoPath,
                webrootPath,
                sp.GetRequiredService<ILogger<LegoCertificateProvider>>()));

        services.AddHostedService<EnvironmentConfigInitializer>();

        var urls = configuration["ASPNETCORE_URLS"] ?? configuration["urls"] ?? "";
        services.Configure<KestrelServerOptions>(options =>
        {
            var maxBodyMb = int.TryParse(configuration["Kestrel:MaxRequestBodySizeMB"], out var mb) ? mb : 100;
            options.Limits.MaxRequestBodySize = maxBodyMb * 1024L * 1024L;

            if (int.TryParse(configuration["Kestrel:MaxConcurrentConnections"], out var maxConn))
            {
                options.Limits.MaxConcurrentConnections = maxConn;
            }
            else
            {
                options.Limits.MaxConcurrentConnections = 10_000;
            }

            if (int.TryParse(configuration["Kestrel:MaxConcurrentUpgradedConnections"], out var maxUpgraded))
            {
                options.Limits.MaxConcurrentUpgradedConnections = maxUpgraded;
            }
            else
            {
                options.Limits.MaxConcurrentUpgradedConnections = 1_000;
            }

            var headerTimeoutSec = int.TryParse(configuration["Kestrel:RequestHeadersTimeoutSeconds"], out var ht) ? ht : 30;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(headerTimeoutSec);
            options.Limits.MaxRequestHeaderCount = 100;
            options.Limits.MaxRequestHeadersTotalSize = 64 * 1024;

            if (urls.Contains("https", StringComparison.OrdinalIgnoreCase))
            {
                options.ConfigureHttpsDefaults(adapterOptions =>
                {
                    var selector = options.ApplicationServices.GetRequiredService<SniCertificateSelector>();
                    adapterOptions.ServerCertificateSelector = (_, hostname) => selector.Select(hostname);
                });
            }
        });

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
