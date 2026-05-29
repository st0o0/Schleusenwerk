using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public sealed class NginxFixture : IAsyncLifetime
{
    private IContainer _container = null!;
    public string BaseUrl { get; private set; } = null!;
    public int Port { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("nginx:alpine")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();
        await _container.StartAsync();
        Port = _container.GetMappedPublicPort(80);
        BaseUrl = $"http://localhost:{Port}";
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }
}
