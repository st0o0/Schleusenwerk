using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
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
        _network = new NetworkBuilder()
            .WithName($"toxiproxy-{Guid.NewGuid():N}")
            .Build();

        _echoBackend = new ContainerBuilder()
            .WithImage("ealen/echo-server:latest")
            .WithNetwork(_network)
            .WithNetworkAliases("echo-backend")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        _toxiproxy = new ContainerBuilder()
            .WithImage("ghcr.io/shopify/toxiproxy:latest")
            .WithNetwork(_network)
            .WithPortBinding(8474, true)
            .WithPortBinding(18080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8474).ForPath("/version")))
            .Build();

        // Start sequentially — network must exist before containers join it
        await _echoBackend.StartAsync();
        await _toxiproxy.StartAsync();

        ApiPort = _toxiproxy.GetMappedPublicPort(8474);
        ApiHost = _toxiproxy.Hostname;
        ProxyPort = _toxiproxy.GetMappedPublicPort(18080);
        ProxyUrl = $"http://{_toxiproxy.Hostname}:{ProxyPort}";

        using var client = CreateClient();
        await client.CreateProxyAsync("echo", "0.0.0.0:18080", "echo-backend:80");
    }

    public ToxiproxyClient CreateClient() => new(ApiHost, ApiPort);

    public async Task ResetAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        await client.ResetAsync("echo", ct);
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
