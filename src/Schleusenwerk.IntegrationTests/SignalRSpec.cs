using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class SignalRSpec
{
    private readonly SchleusenwerkFixture _fixture;

    public SignalRSpec(SchleusenwerkFixture fixture) => _fixture = fixture;

    [Fact(Timeout = 30_000)]
    public async Task Should_receive_event_when_route_created()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var hub = _fixture.CreateHubConnection();
        var eventReceived = new TaskCompletionSource<JsonElement>();
        hub.On<JsonElement>("OnProxyEvent", evt => eventReceived.TrySetResult(evt));
        await hub.StartAsync(ct);

        var domain = $"signalr-{Guid.NewGuid():N}.test";
        await _fixture.Client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 }), Encoding.UTF8, "application/json"), ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => eventReceived.TrySetCanceled());

        var evt = await eventReceived.Task;
        Assert.Equal("RouteUpdated", evt.GetProperty("type").GetString());
    }
}
