using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public sealed class WebSocketEchoFixture : IAsyncLifetime
{
    private IContainer _container = null!;
    public string BaseUrl { get; private set; } = null!;
    public int Port { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("jmalloc/echo-server:latest")
            .WithEnvironment("PORT", "8080")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080)))
            .Build();
        await _container.StartAsync();
        Port = _container.GetMappedPublicPort(8080);
        BaseUrl = $"http://localhost:{Port}";
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }
}
