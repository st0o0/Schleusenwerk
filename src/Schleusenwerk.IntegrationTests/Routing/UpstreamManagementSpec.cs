using System.Text;
using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Routing;

[Collection("Integration")]
public sealed class UpstreamManagementSpec
{
    private readonly HttpClient _client;
    public UpstreamManagementSpec(SchleusenwerkTestHost host) => _client = host.Client;

    [Fact(Timeout = 30_000)]
    public async Task Should_add_upstream_to_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("ups-add");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://first:8080", ct: ct);
        await TestHelper.AddUpstreamAsync(_client, domain, "http://second:8081", ct);
        var response = await _client.GetAsync($"/api/routes/{domain}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(2, detail.GetProperty("upstreams").GetArrayLength());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_remove_upstream_from_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("ups-rm");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://removeme:9090", ct: ct);
        var detailBefore = await _client.GetAsync($"/api/routes/{domain}", ct);
        var jsonBefore = await detailBefore.Content.ReadAsStringAsync(ct);
        var actualUrl = JsonSerializer.Deserialize<JsonElement>(jsonBefore).GetProperty("upstreams")[0].GetProperty("url").GetString()!;
        var encodedUrl = TestHelper.EncodeUpstreamUrl(actualUrl);
        await _client.DeleteAsync($"/api/routes/{domain}/upstreams/{encodedUrl}", ct);
        var response = await _client.GetAsync($"/api/routes/{domain}", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(0, detail.GetProperty("upstreams").GetArrayLength());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_fail_adding_upstream_to_nonexistent_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = JsonSerializer.Serialize(new { url = "http://backend:8080", weight = 1 });
        var response = await _client.PostAsync("/api/routes/nonexistent.test/upstreams", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(result.GetProperty("success").GetBoolean());
    }
}
