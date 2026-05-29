using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Resilience;

[Collection("Integration")]
public sealed class HealthCheckSpec
{
    private readonly SchleusenwerkTestHost _host;
    private readonly ToxiproxyFixture _toxiproxy;

    public HealthCheckSpec(SchleusenwerkTestHost host, ToxiproxyFixture toxiproxy)
    {
        _host = host;
        _toxiproxy = toxiproxy;
    }

    [Fact(Timeout = 120_000)]
    public async Task Upstream_should_become_healthy_after_probes_succeed()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("health-success");

        // Register route pointing to toxiproxy (which is healthy)
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, ct: ct);

        try
        {
            // Wait for upstream to become healthy
            var isHealthy = await TestHelper.WaitForHealthyAsync(
                _host.Client,
                domain,
                TimeSpan.FromSeconds(30),
                ct: ct);

            Assert.True(isHealthy, "Upstream should become healthy after successful health probes");
        }
        finally
        {
            await TestHelper.RemoveRouteAsync(_host.Client, domain, ct: ct);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Health_api_should_report_upstream_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("health-api");

        // Register route pointing to toxiproxy
        await TestHelper.RegisterRouteAsync(_host.Client, domain, _toxiproxy.ProxyUrl, ct: ct);

        try
        {
            // Wait for upstream to become healthy
            var isHealthy = await TestHelper.WaitForHealthyAsync(
                _host.Client,
                domain,
                TimeSpan.FromSeconds(30),
                ct: ct);

            Assert.True(isHealthy, "Upstream should be healthy");

            // Query health API and verify it reports healthy status
            var response = await _host.Client.GetAsync($"/api/health/{domain}", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var healthData = JsonSerializer.Deserialize<JsonElement>(json);

            // Verify health data structure and status
            Assert.True(healthData.TryGetProperty("upstreams", out var upstreamsElement),
                "Health response should contain 'upstreams' property");

            var upstreams = upstreamsElement.EnumerateArray().ToList();
            Assert.True(upstreams.Count > 0, "Should have at least one upstream");

            // All upstreams should be marked as healthy
            foreach (var upstream in upstreams)
            {
                Assert.True(upstream.GetProperty("isHealthy").GetBoolean(),
                    "Upstream should be marked as healthy");
            }
        }
        finally
        {
            await TestHelper.RemoveRouteAsync(_host.Client, domain, ct: ct);
        }
    }
}
