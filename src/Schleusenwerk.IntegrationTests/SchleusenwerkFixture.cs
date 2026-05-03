using System.Net.Security;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Schleusenwerk.Contracts;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

public sealed class SchleusenwerkFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public GrpcChannel GrpcChannel { get; private set; } = null!;
    public HttpClient ProxyHttp { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Schleusenwerk_AppHost>();

        App = await builder.BuildAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await App.StartAsync(cts.Token);

        var grpcEndpoint = App.GetEndpoint("proxy", "grpc");
        GrpcChannel = GrpcChannel.ForAddress(grpcEndpoint, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }
            }
        });

        var httpEndpoint = App.GetEndpoint("proxy", "http");
        ProxyHttp = new HttpClient { BaseAddress = httpEndpoint };

        await WaitForProxyReady(cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        GrpcChannel.Dispose();
        ProxyHttp.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }

    private async Task WaitForProxyReady(CancellationToken ct)
    {
        var client = new RouteService.RouteServiceClient(GrpcChannel);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await client.ListRoutesAsync(new Empty(), cancellationToken: ct);
                return;
            }
            catch
            {
                await Task.Delay(500, ct);
            }
        }
    }
}

[CollectionDefinition("Schleusenwerk")]
public sealed class SchleusenwerkCollection : ICollectionFixture<SchleusenwerkFixture>;
