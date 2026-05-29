using System.Net;
using System.Text;
using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Routing;

[Collection("Integration")]
public sealed class RouteManagementSpec
{
    private readonly HttpClient _client;
    public RouteManagementSpec(SchleusenwerkTestHost host) => _client = host.Client;

    [Fact(Timeout = 30_000)]
    public async Task Should_create_route_with_upstream()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("create");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        var response = await _client.GetAsync($"/api/routes/{domain}", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(domain, detail.GetProperty("domain").GetString());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_list_all_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("list");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        var response = await _client.GetAsync("/api/routes", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var routes = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Contains(routes.EnumerateArray(), r => r.GetProperty("domain").GetString() == domain);
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_get_route_detail()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("detail");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        var response = await _client.GetAsync($"/api/routes/{domain}", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(domain, detail.GetProperty("domain").GetString());
        Assert.True(detail.TryGetProperty("upstreams", out _));
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_update_route_config()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("update");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        var updateBody = JsonSerializer.Serialize(new { forceHttps = true, timeoutSeconds = 120 });
        await _client.PutAsync($"/api/routes/{domain}", new StringContent(updateBody, Encoding.UTF8, "application/json"), ct);
        var response = await _client.GetAsync($"/api/routes/{domain}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(detail.GetProperty("forceHttps").GetBoolean());
        Assert.Equal(120, detail.GetProperty("timeoutSeconds").GetInt32());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_delete_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("delete");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        await _client.DeleteAsync($"/api/routes/{domain}", ct);
        var response = await _client.GetAsync("/api/routes", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var routes = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.DoesNotContain(routes.EnumerateArray(), r => r.GetProperty("domain").GetString() == domain);
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_return_404_for_unknown_route()
    {
        var response = await _client.GetAsync("/api/routes/nonexistent.test", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
