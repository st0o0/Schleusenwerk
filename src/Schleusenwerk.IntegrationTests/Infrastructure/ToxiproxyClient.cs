using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public sealed class ToxiproxyClient : IDisposable
{
    private readonly HttpClient _http;

    public ToxiproxyClient(string host, int port)
    {
        _http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}") };
    }

    public async Task CreateProxyAsync(string name, string listen, string upstream, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { name, listen, upstream, enabled = true });
        var response = await _http.PostAsync("/proxies",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddToxicAsync(string proxyName, string toxicName, string type, string stream,
        Dictionary<string, object> attributes, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["name"] = toxicName,
            ["type"] = type,
            ["stream"] = stream,
            ["attributes"] = attributes,
        };
        var body = JsonSerializer.Serialize(payload);
        var response = await _http.PostAsync($"/proxies/{proxyName}/toxics",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    public Task AddLatencyAsync(string proxyName, string toxicName, int latencyMs,
        string stream = "upstream", CancellationToken ct = default)
        => AddToxicAsync(proxyName, toxicName, "latency", stream,
            new Dictionary<string, object> { ["latency"] = latencyMs, ["jitter"] = 0 }, ct);

    public Task AddResetPeerAsync(string proxyName, string toxicName, int timeoutMs = 0,
        string stream = "upstream", CancellationToken ct = default)
        => AddToxicAsync(proxyName, toxicName, "reset_peer", stream,
            new Dictionary<string, object> { ["timeout"] = timeoutMs }, ct);

    public Task AddTimeoutAsync(string proxyName, string toxicName, int timeoutMs,
        string stream = "upstream", CancellationToken ct = default)
        => AddToxicAsync(proxyName, toxicName, "timeout", stream,
            new Dictionary<string, object> { ["timeout"] = timeoutMs }, ct);

    public async Task RemoveToxicAsync(string proxyName, string toxicName, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/proxies/{proxyName}/toxics/{toxicName}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task EnableProxyAsync(string proxyName, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { enabled = true });
        var response = await _http.PostAsync($"/proxies/{proxyName}",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DisableProxyAsync(string proxyName, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { enabled = false });
        var response = await _http.PostAsync($"/proxies/{proxyName}",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetAsync(string proxyName, CancellationToken ct = default)
    {
        await EnableProxyAsync(proxyName, ct);

        var response = await _http.GetAsync($"/proxies/{proxyName}/toxics", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var toxics = JsonSerializer.Deserialize<JsonElement>(json);

        foreach (var toxic in toxics.EnumerateArray())
        {
            var name = toxic.GetProperty("name").GetString()!;
            await RemoveToxicAsync(proxyName, name, ct);
        }
    }

    public void Dispose() => _http.Dispose();
}
