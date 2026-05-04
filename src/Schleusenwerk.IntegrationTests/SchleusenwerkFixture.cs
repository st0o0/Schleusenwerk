using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

public sealed class SchleusenwerkFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public Uri ApiBaseUri { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Schleusenwerk_AppHost>();

        App = await builder.BuildAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await App.StartAsync(cts.Token);

        ApiBaseUri = App.GetEndpoint("proxy", "api");
        Client = new HttpClient { BaseAddress = ApiBaseUri };

        await WaitForReady(cts.Token);
    }

    public HubConnection CreateHubConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(ApiBaseUri, "/hubs/events"))
            .Build();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }

    private async Task WaitForReady(CancellationToken ct)
    {
        Exception? lastException = null;
        int lastStatusCode = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await Client.GetAsync("/api/health", ct);
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

            await Task.Delay(2000, ct);
        }

        throw new TimeoutException(
            $"Proxy did not become ready. BaseAddress={Client.BaseAddress}, LastStatus={lastStatusCode}, LastError={lastException?.Message}");
    }
}

[CollectionDefinition("Schleusenwerk")]
public sealed class SchleusenwerkCollection : ICollectionFixture<SchleusenwerkFixture>;
