using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Toxiproxy.Net;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public sealed class ToxiproxyFixture : IAsyncLifetime
{
    private INetwork _network = null!;
    private IContainer _echoBackend = null!;
    private IContainer _toxiproxy = null!;

    public string ProxyUrl { get; private set; } = null!;
    public int ProxyPort { get; private set; }
    public string ApiHost { get; private set; } = null!;
    public int ApiPort { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _network = new NetworkBuilder().WithName($"toxiproxy-{Guid.NewGuid():N}").Build();
        await _network.CreateAsync();

        _echoBackend = new ContainerBuilder()
            .WithImage("ealen/echo-server:latest")
            .WithNetwork(_network)
            .WithNetworkAliases("echo-backend")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        _toxiproxy = new ContainerBuilder()
            .WithImage("ghcr.io/shopify/toxiproxy:latest")
            .WithNetwork(_network)
            .WithPortBinding(8474, true)
            .WithPortBinding(18080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8474)))
            .Build();

        await Task.WhenAll(_echoBackend.StartAsync(), _toxiproxy.StartAsync());

        ApiPort = _toxiproxy.GetMappedPublicPort(8474);
        ApiHost = "localhost";
        ProxyPort = _toxiproxy.GetMappedPublicPort(18080);
        ProxyUrl = $"http://localhost:{ProxyPort}";

        // Create the proxy that forwards to echo-backend
        var connection = new Connection(ApiHost, ApiPort);
        var client = connection.Client();
        client.Add(new Proxy
        {
            Name = "echo",
            Listen = "0.0.0.0:18080",
            Upstream = "echo-backend:80",
            Enabled = true,
        });
    }

    public Connection CreateConnection() => new(ApiHost, ApiPort);

    public void Reset()
    {
        var client = new Connection(ApiHost, ApiPort).Client();
        var proxy = client.FindProxy("echo");
        proxy.Enabled = true;
        proxy.Update();
        var toxics = proxy.GetAllToxics();
        foreach (var toxic in toxics)
        {
            proxy.RemoveToxic(toxic.Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _toxiproxy.StopAsync();
        await _toxiproxy.DisposeAsync();
        await _echoBackend.StopAsync();
        await _echoBackend.DisposeAsync();
        await _network.DeleteAsync();
        await _network.DisposeAsync();
    }
}
