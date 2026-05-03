using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class HealthSpec
{
    private readonly HttpClient _client;

    public HealthSpec(SchleusenwerkFixture fixture) => _client = fixture.Client;

    [Fact(Timeout = 30_000)]
    public async Task GetHealth_should_return_counts()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(health.TryGetProperty("routeCount", out _));
        Assert.True(health.TryGetProperty("healthyCount", out _));
        Assert.True(health.TryGetProperty("unhealthyCount", out _));
    }

    [Fact(Timeout = 30_000)]
    public async Task GetUpstreamHealth_should_return_entries_for_domain()
    {
        var domain = $"health-{Guid.NewGuid():N}.test";
        await _client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, timeoutSeconds = 30, firstUpstreamUrl = "http://backend:8080" }), Encoding.UTF8, "application/json"));
        var response = await _client.GetAsync($"/api/health/{domain}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(domain, health.GetProperty("domain").GetString());
        Assert.Equal(JsonValueKind.Array, health.GetProperty("upstreams").ValueKind);
    }
}
