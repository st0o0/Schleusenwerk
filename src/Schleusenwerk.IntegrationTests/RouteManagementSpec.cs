using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class RouteManagementSpec
{
    private readonly HttpClient _client;

    public RouteManagementSpec(SchleusenwerkFixture fixture) => _client = fixture.Client;

    [Fact(Timeout = 30_000)]
    public async Task ListRoutes_should_return_empty_initially()
    {
        var response = await _client.GetAsync("/api/routes");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var routes = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(JsonValueKind.Array, routes.ValueKind);
    }

    [Fact(Timeout = 30_000)]
    public async Task AddRoute_should_create_and_return_success()
    {
        var domain = $"add-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, forceHttps = true, timeoutSeconds = 30 });
        var response = await _client.PostAsync("/api/routes", new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(result.GetProperty("success").GetBoolean());
    }

    [Fact(Timeout = 30_000)]
    public async Task ListRoutes_should_contain_created_route()
    {
        var domain = $"list-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 });
        await _client.PostAsync("/api/routes", new StringContent(body, Encoding.UTF8, "application/json"));
        var response = await _client.GetAsync("/api/routes");
        var json = await response.Content.ReadAsStringAsync();
        var routes = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(routes.EnumerateArray().Any(r => r.GetProperty("domain").GetString() == domain));
    }

    [Fact(Timeout = 30_000)]
    public async Task GetRoute_should_return_detail()
    {
        var domain = $"detail-{Guid.NewGuid():N}.test";
        var body = JsonSerializer.Serialize(new { domain, forceHttps = true, timeoutSeconds = 60 });
        await _client.PostAsync("/api/routes", new StringContent(body, Encoding.UTF8, "application/json"));
        var response = await _client.GetAsync($"/api/routes/{domain}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(domain, detail.GetProperty("domain").GetString());
        Assert.True(detail.GetProperty("forceHttps").GetBoolean());
        Assert.Equal(60, detail.GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact(Timeout = 30_000)]
    public async Task UpdateRoute_should_change_config()
    {
        var domain = $"update-{Guid.NewGuid():N}.test";
        await _client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, forceHttps = false, timeoutSeconds = 30 }), Encoding.UTF8, "application/json"));
        await _client.PutAsync($"/api/routes/{domain}", new StringContent(JsonSerializer.Serialize(new { forceHttps = true, timeoutSeconds = 120 }), Encoding.UTF8, "application/json"));
        var response = await _client.GetAsync($"/api/routes/{domain}");
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(detail.GetProperty("forceHttps").GetBoolean());
        Assert.Equal(120, detail.GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact(Timeout = 30_000)]
    public async Task DeleteRoute_should_remove_route()
    {
        var domain = $"delete-{Guid.NewGuid():N}.test";
        await _client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 }), Encoding.UTF8, "application/json"));
        await _client.DeleteAsync($"/api/routes/{domain}");
        var response = await _client.GetAsync("/api/routes");
        var json = await response.Content.ReadAsStringAsync();
        var routes = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(routes.EnumerateArray().Any(r => r.GetProperty("domain").GetString() == domain));
    }

    [Fact(Timeout = 30_000)]
    public async Task GetRoute_should_return_404_for_unknown_domain()
    {
        var response = await _client.GetAsync("/api/routes/nonexistent.test");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
