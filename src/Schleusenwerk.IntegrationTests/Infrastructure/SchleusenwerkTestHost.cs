using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Startup;
using Servus.Core.Application.Startup;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public sealed class SchleusenwerkTestHost : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _tempCertsDirectory;
    private string? _tempWebrootDirectory;

    public HttpClient Client { get; private set; } = null!;
    public Uri BaseUri { get; private set; } = null!;
    public IServiceProvider Services => _app?.Services ?? throw new InvalidOperationException("Host not initialized");

    public async ValueTask InitializeAsync()
    {
        // Create temporary directories for test isolation
        _tempCertsDirectory = Path.Combine(Path.GetTempPath(), $"schleusenwerk-test-{Guid.NewGuid():N}");
        _tempWebrootDirectory = Path.Combine(Path.GetTempPath(), $"schleusenwerk-webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempCertsDirectory);
        Directory.CreateDirectory(_tempWebrootDirectory);

        try
        {
            // Build the WebApplicationBuilder with test configuration
            var builder = WebApplication.CreateBuilder();

            // Override configuration with test values
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Isolated in-memory SQLite database per test
                ["Akka:Persistence:ConnectionString"] = $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",

                // Use temp directories for certs and webroot
                ["Certificates:Path"] = _tempCertsDirectory,
                ["Lego:WebrootPath"] = _tempWebrootDirectory,

                // Disable Docker discovery
                ["Docker:SocketPath"] = "",

                // Local remoting with OS-assigned port
                ["Akka:Remoting:Hostname"] = "127.0.0.1",
                ["Akka:Remoting:Port"] = "0",

                // Listen on random port
                ["ASPNETCORE_URLS"] = "http://127.0.0.1:0",

                // Allow all CORS origins for testing
                ["Cors:AllowedOrigins"] = "*",
            });

            // Call setup classes in order
            var servicesSetup = new SchleusenwerkServicesSetup();
            servicesSetup.SetupServices(builder.Services, builder.Configuration);

            var actorSystemSetup = new SchleusenwerkActorSystemSetup();
            ((IServiceSetupContainer)actorSystemSetup).SetupServices(builder.Services, builder.Configuration);

            // Build the application
            _app = builder.Build();

            // Apply application middleware
            var appSetup = new SchleusenwerkApplicationSetup();
            appSetup.SetupApplicationInternal(_app);

            await _app.StartAsync();

            var url = _app.Urls.FirstOrDefault()
                ?? throw new InvalidOperationException("Kestrel did not bind to any URL");

            BaseUri = new Uri(url);
            Client = new HttpClient { BaseAddress = BaseUri };

            await WaitForReady(CancellationToken.None);
        }
        catch
        {
            // Clean up on failure
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
            CleanupTempDirectories();
            throw;
        }
    }

    public HubConnection CreateHubConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(BaseUri, "/hubs/events"))
            .Build();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

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
            // Suppress cleanup errors
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
            // Suppress cleanup errors
        }
    }

    private async Task WaitForReady(CancellationToken ct)
    {
        Exception? lastException = null;
        int lastStatusCode = 0;
        const int maxAttempts = 60; // 60 * 500ms = 30 seconds
        int attempts = 0;

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
            $"Proxy did not become ready. BaseUri={BaseUri}, LastStatus={lastStatusCode}, LastError={lastException?.Message}");
    }
}
