using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Api;

[Collection("Integration")]
public sealed class HealthEndpointSpec
{
    private readonly HttpClient _client;
    public HealthEndpointSpec(SchleusenwerkTestHost host) => _client = host.Client;

    [Fact(Timeout = 30_000)]
    public async Task Health_endpoint_should_return_ok()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/health", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var health = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(health.TryGetProperty("routeCount", out _));
    }

    [Fact(Timeout = 30_000)]
    public async Task Health_endpoint_should_return_domain_health()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("health-api");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        var response = await _client.GetAsync($"/api/health/{domain}", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var health = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(domain, health.GetProperty("domain").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task Health_endpoint_should_return_empty_upstreams_for_unknown_domain()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/health/nonexistent.test", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var health = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(0, health.GetProperty("upstreams").GetArrayLength());
    }
}
