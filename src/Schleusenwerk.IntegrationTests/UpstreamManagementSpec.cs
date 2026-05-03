using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class UpstreamManagementSpec
{
    private readonly HttpClient _client;

    public UpstreamManagementSpec(SchleusenwerkFixture fixture) => _client = fixture.Client;

    [Fact(Timeout = 30_000)]
    public async Task AddUpstream_should_appear_in_route_detail()
    {
        var domain = $"ups-add-{Guid.NewGuid():N}.test";
        await _client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 }), Encoding.UTF8, "application/json"));
        await _client.PostAsync($"/api/routes/{domain}/upstreams", new StringContent(JsonSerializer.Serialize(new { url = "http://backend:8080", weight = 1 }), Encoding.UTF8, "application/json"));
        var response = await _client.GetAsync($"/api/routes/{domain}");
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(detail.GetProperty("upstreams").EnumerateArray().Any(u => u.GetProperty("url").GetString()!.Contains("backend:8080")));
    }

    [Fact(Timeout = 30_000)]
    public async Task RemoveUpstream_should_disappear_from_route_detail()
    {
        var domain = $"ups-rm-{Guid.NewGuid():N}.test";
        var upstreamUrl = "http://removeme:9090";
        await _client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, timeoutSeconds = 30, firstUpstreamUrl = upstreamUrl }), Encoding.UTF8, "application/json"));

        var detailBefore = await _client.GetAsync($"/api/routes/{domain}");
        var jsonBefore = await detailBefore.Content.ReadAsStringAsync();
        var actualUrl = JsonSerializer.Deserialize<JsonElement>(jsonBefore)
            .GetProperty("upstreams")[0].GetProperty("url").GetString()!;

        var encodedUrl = Convert.ToBase64String(Encoding.UTF8.GetBytes(actualUrl)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await _client.DeleteAsync($"/api/routes/{domain}/upstreams/{encodedUrl}");
        var response = await _client.GetAsync($"/api/routes/{domain}");
        var json = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(0, detail.GetProperty("upstreams").GetArrayLength());
    }

    [Fact(Timeout = 30_000)]
    public async Task AddUpstream_to_nonexistent_route_should_fail()
    {
        var response = await _client.PostAsync("/api/routes/nonexistent.test/upstreams", new StringContent(JsonSerializer.Serialize(new { url = "http://backend:8080", weight = 1 }), Encoding.UTF8, "application/json"));
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(result.GetProperty("success").GetBoolean());
    }
}
