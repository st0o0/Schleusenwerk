using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class DiscoverySpec
{
    private readonly HttpClient _client;

    public DiscoverySpec(SchleusenwerkFixture fixture) => _client = fixture.Client;

    [Fact(Timeout = 30_000)]
    public async Task ListContainers_should_return_ok()
    {
        var response = await _client.GetAsync("/api/discovery/containers", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Array, JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ValueKind);
    }
}
