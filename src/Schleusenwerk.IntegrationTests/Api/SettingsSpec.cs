using System.Text;
using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Api;

[Collection("Integration")]
public sealed class SettingsSpec
{
    private readonly HttpClient _client;
    public SettingsSpec(SchleusenwerkTestHost host) => _client = host.Client;

    [Fact(Timeout = 30_000)]
    public async Task Should_get_default_settings()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/settings", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var settings = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(settings.TryGetProperty("stage", out _));
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_update_settings()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = JsonSerializer.Serialize(new { stage = "staging" });
        var response = await _client.PutAsync("/api/settings", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    [Fact(Timeout = 30_000)]
    public async Task Settings_should_persist_across_reads()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = JsonSerializer.Serialize(new { stage = "local" });
        await _client.PutAsync("/api/settings", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        var response = await _client.GetAsync("/api/settings", ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var settings = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("local", settings.GetProperty("stage").GetString());
    }
}
