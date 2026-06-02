using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Startup;
using Servus.Core.Application.Startup;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public sealed class DockerDiscoveryTestHost : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _tempCertsDirectory;
    private string? _tempWebrootDirectory;

    public HttpClient Client { get; private set; } = null!;
    public Uri BaseUri { get; private set; } = null!;
    public IServiceProvider Services => _app?.Services ?? throw new InvalidOperationException("Host not initialized");

    public async ValueTask InitializeAsync()
    {
        DockerAvailableGuard.SkipIfUnavailable();

        _tempCertsDirectory = Path.Combine(Path.GetTempPath(), $"schleusenwerk-docker-test-{Guid.NewGuid():N}");
        _tempWebrootDirectory = Path.Combine(Path.GetTempPath(), $"schleusenwerk-docker-webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempCertsDirectory);
        Directory.CreateDirectory(_tempWebrootDirectory);

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Akka:Persistence:ConnectionString"] = $"Data Source=docker-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
                ["Certificates:Path"] = _tempCertsDirectory,
                ["Lego:WebrootPath"] = _tempWebrootDirectory,
                ["Docker:Enabled"] = "true",
                ["Akka:Remoting:Hostname"] = "127.0.0.1",
                ["Akka:Remoting:Port"] = FindFreePort().ToString(),
                ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",
                ["Cors:AllowedOrigins"] = "http://localhost:3000,http://localhost:5173,http://127.0.0.1:0",
            });

            var kestrelPort = FindFreePort();
            builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(k =>
            {
                k.Listen(IPAddress.Loopback, kestrelPort);
            });

            var servicesSetup = new SchleusenwerkServicesSetup();
            servicesSetup.SetupServices(builder.Services, builder.Configuration);

            builder.Services.AddControllers()
                .AddApplicationPart(typeof(SchleusenwerkServicesSetup).Assembly);

            var actorSystemSetup = new SchleusenwerkActorSystemSetup();
            ((IServiceSetupContainer)actorSystemSetup).SetupServices(builder.Services, builder.Configuration);

            _app = builder.Build();

            var appSetup = new SchleusenwerkApplicationSetup();
            appSetup.SetupApplicationInternal(_app);

            await _app.StartAsync();

            var server = _app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
                ?? throw new InvalidOperationException("No server addresses available");
            var url = addresses.FirstOrDefault()
                ?? throw new InvalidOperationException("Kestrel did not bind to any URL");

            BaseUri = new Uri(url);
            Client = new HttpClient { BaseAddress = BaseUri };

            await WaitForReady(CancellationToken.None);
        }
        catch
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
            CleanupTempDirectories();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        CleanupTempDirectories();
    }

    private void CleanupTempDirectories()
    {
        try
        {
            if (!string.IsNullOrEmpty(_tempCertsDirectory) && Directory.Exists(_tempCertsDirectory))
            {
                Directory.Delete(_tempCertsDirectory, recursive: true);
            }
        }
        catch
        {
        }

        try
        {
            if (!string.IsNullOrEmpty(_tempWebrootDirectory) && Directory.Exists(_tempWebrootDirectory))
            {
                Directory.Delete(_tempWebrootDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task WaitForReady(CancellationToken ct)
    {
        Exception? lastException = null;
        var lastStatusCode = 0;
        const int maxAttempts = 60;
        var attempts = 0;

        while (attempts < maxAttempts)
        {
            try
            {
                var response = await Client.GetAsync("/health", ct);
                lastStatusCode = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(500, ct);
            attempts++;
        }

        throw new TimeoutException(
            $"DockerDiscoveryTestHost did not become ready. BaseUri={BaseUri}, LastStatus={lastStatusCode}, LastError={lastException?.Message}");
    }
}
