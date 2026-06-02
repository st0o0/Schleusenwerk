using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Startup;
using Servus.Core.Application.Startup;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Startup;

public sealed class EnvironmentConfigInitializerSpec
{
    [Fact(Timeout = 120_000)]
    public async Task Host_should_start_with_DOMAINS_env_var()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = $"env-test-{Guid.NewGuid():N}.test";
        var original = Environment.GetEnvironmentVariable("DOMAINS");

        try
        {
            Environment.SetEnvironmentVariable("DOMAINS", $"{domain} -> http://backend:8080");

            await using var host = await BootHostAsync(ct);

            var found = await PollForRouteAsync(host.Client, domain, TimeSpan.FromSeconds(30), ct);
            Assert.True(found, $"Domain '{domain}' should be registered from DOMAINS env var");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOMAINS", original);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Host_should_start_with_multiple_DOMAINS_entries()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain1 = $"multi1-{Guid.NewGuid():N}.test";
        var domain2 = $"multi2-{Guid.NewGuid():N}.test";
        var original = Environment.GetEnvironmentVariable("DOMAINS");

        try
        {
            Environment.SetEnvironmentVariable("DOMAINS",
                $"{domain1} -> http://backend1:8080, {domain2} -> http://backend2:9090");

            await using var host = await BootHostAsync(ct);

            var found1 = await PollForRouteAsync(host.Client, domain1, TimeSpan.FromSeconds(30), ct);
            var found2 = await PollForRouteAsync(host.Client, domain2, TimeSpan.FromSeconds(30), ct);
            Assert.True(found1, $"Domain '{domain1}' should be registered");
            Assert.True(found2, $"Domain '{domain2}' should be registered");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOMAINS", original);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Host_should_start_without_DOMAINS_env_var()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = Environment.GetEnvironmentVariable("DOMAINS");

        try
        {
            Environment.SetEnvironmentVariable("DOMAINS", null);

            await using var host = await BootHostAsync(ct);

            var response = await host.Client.GetAsync("/api/routes", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOMAINS", original);
        }
    }

    private static async Task<bool> PollForRouteAsync(
        HttpClient client, string domain, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var response = await client.GetAsync($"/api/routes/{domain}", cts.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var detail = JsonSerializer.Deserialize<JsonElement>(json);
                    if (detail.GetProperty("domain").GetString() == domain)
                    {
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            await Task.Delay(1000, cts.Token);
        }

        return false;
    }

    private static async Task<TestHost> BootHostAsync(CancellationToken ct)
    {
        var tempCerts = Path.Combine(Path.GetTempPath(), $"schleusenwerk-envtest-{Guid.NewGuid():N}");
        var tempWebroot = Path.Combine(Path.GetTempPath(), $"schleusenwerk-webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempCerts);
        Directory.CreateDirectory(tempWebroot);

        var akkaPort = FindFreePort();
        var kestrelPort = FindFreePort();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Akka:Persistence:ConnectionString"] = $"Data Source=envtest-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
            ["Certificates:Path"] = tempCerts,
            ["Lego:WebrootPath"] = tempWebroot,
            ["Docker:Enabled"] = "false",
            ["Akka:Remoting:Hostname"] = "127.0.0.1",
            ["Akka:Remoting:Port"] = akkaPort.ToString(),
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{kestrelPort}",
        });

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

        var app = builder.Build();

        var appSetup = new SchleusenwerkApplicationSetup();
        appSetup.SetupApplicationInternal(app);

        await app.StartAsync(ct);

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
            ?? throw new InvalidOperationException("No server addresses");
        var url = addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not bind");

        var baseUri = new Uri(url);
        var client = new HttpClient { BaseAddress = baseUri };

        await WaitForReady(client, ct);

        return new TestHost(app, client, tempCerts, tempWebroot);
    }

    private static async Task WaitForReady(HttpClient client, CancellationToken ct)
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                var response = await client.GetAsync("/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // not ready yet
            }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException("Proxy did not become ready");
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _tempCerts;
        private readonly string _tempWebroot;

        public HttpClient Client { get; }

        public TestHost(WebApplication app, HttpClient client, string tempCerts, string tempWebroot)
        {
            _app = app;
            Client = client;
            _tempCerts = tempCerts;
            _tempWebroot = tempWebroot;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();

            try { Directory.Delete(_tempCerts, true); } catch { }
            try { Directory.Delete(_tempWebroot, true); } catch { }
        }
    }
}
