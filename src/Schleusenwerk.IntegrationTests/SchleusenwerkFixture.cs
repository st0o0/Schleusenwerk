using Microsoft.AspNetCore.SignalR.Client;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

/// <summary>
/// Fixture wrapper that uses SchleusenwerkTestHost internally.
/// This maintains API compatibility with existing tests while transitioning to in-process testing.
/// </summary>
public sealed class SchleusenwerkFixture : IAsyncLifetime
{
    private SchleusenwerkTestHost? _host;

    public HttpClient Client => _host?.Client ?? throw new InvalidOperationException("Host not initialized");
    public Uri ApiBaseUri => _host?.BaseUri ?? throw new InvalidOperationException("Host not initialized");

    public async ValueTask InitializeAsync()
    {
        _host = new SchleusenwerkTestHost();
        await _host.InitializeAsync();
    }

    public HubConnection CreateHubConnection()
    {
        return _host?.CreateHubConnection() ?? throw new InvalidOperationException("Host not initialized");
    }

    public async ValueTask DisposeAsync()
    {
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
    }
}

[CollectionDefinition("Schleusenwerk")]
public sealed class SchleusenwerkCollection : ICollectionFixture<SchleusenwerkFixture>;
