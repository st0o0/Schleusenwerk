using System.Text;
using System.Text.Json;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

public static class TestHelper
{
    public static string UniqueDomain(string prefix = "test")
        => $"{prefix}-{Guid.NewGuid():N}.test";

    public static async Task RegisterRouteAsync(
        HttpClient apiClient,
        string domain,
        string upstreamUrl,
        string tlsMode = "selfsigned",
        bool webSocketEnabled = false,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            domain,
            forceHttps = false,
            webSocketEnabled,
            timeoutSeconds,
            tlsMode,
            firstUpstreamUrl = upstreamUrl,
        });

        var response = await apiClient.PostAsync(
            "/api/routes",
            new StringContent(body, Encoding.UTF8, "application/json"),
            ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        if (!result.GetProperty("success").GetBoolean())
        {
            throw new InvalidOperationException($"Route registration failed: {result}");
        }
    }

    public static async Task AddUpstreamAsync(
        HttpClient apiClient,
        string domain,
        string upstreamUrl,
        CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { url = upstreamUrl, weight = 1 });
        var response = await apiClient.PostAsync(
            $"/api/routes/{domain}/upstreams",
            new StringContent(body, Encoding.UTF8, "application/json"),
            ct);
        response.EnsureSuccessStatusCode();
    }

    public static async Task RemoveRouteAsync(
        HttpClient apiClient,
        string domain,
        CancellationToken ct = default)
    {
        await apiClient.DeleteAsync($"/api/routes/{domain}", ct);
    }

    public static HttpClient CreateProxyClient(Uri baseUri, string domain)
    {
        var client = new HttpClient { BaseAddress = baseUri };
        client.DefaultRequestHeaders.Host = domain;
        return client;
    }

    public static async Task<bool> WaitForHealthyAsync(
        HttpClient apiClient,
        string domain,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var response = await apiClient.GetAsync($"/api/health/{domain}", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var health = JsonSerializer.Deserialize<JsonElement>(json);
                    var upstreams = health.GetProperty("upstreams");

                    if (upstreams.GetArrayLength() > 0 &&
                        upstreams.EnumerateArray().All(u => u.GetProperty("isHealthy").GetBoolean()))
                    {
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            await Task.Delay(2000, cts.Token);
        }

        return false;
    }

    public static string EncodeUpstreamUrl(string url)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
