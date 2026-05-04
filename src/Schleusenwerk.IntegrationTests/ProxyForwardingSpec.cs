using System.Net;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class ProxyForwardingSpec : IAsyncLifetime
{
    private readonly SchleusenwerkFixture _fixture;
    private IContainer _upstream = null!;
    private string _upstreamHostUrl = null!;
    private readonly string _domain = $"forward-{Guid.NewGuid():N}.test";

    public ProxyForwardingSpec(SchleusenwerkFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        _upstream = new ContainerBuilder()
            .WithImage("nginx:alpine")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        await _upstream.StartAsync();

        var hostPort = _upstream.GetMappedPublicPort(80);
        _upstreamHostUrl = $"http://host.docker.internal:{hostPort}";
    }

    public async ValueTask DisposeAsync()
    {
        var apiClient = _fixture.Client;
        await apiClient.DeleteAsync($"/api/routes/{_domain}");
        await _upstream.StopAsync();
        await _upstream.DisposeAsync();
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_forward_request_to_upstream_container()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(ct: ct);

        using var proxyClient = CreateProxyClient();
        var response = await proxyClient.GetAsync("/", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("nginx", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_forward_subpath_to_upstream()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(ct: ct);

        using var proxyClient = CreateProxyClient();
        var response = await proxyClient.GetAsync("/nonexistent-path", ct);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"Expected 200 or 404 from upstream, got {(int)response.StatusCode}");
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_set_forwarded_headers()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var echoContainer = new ContainerBuilder()
            .WithImage("ealen/echo-server:latest")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        await echoContainer.StartAsync(ct);

        var echoPort = echoContainer.GetMappedPublicPort(80);
        var echoDomain = $"echo-{Guid.NewGuid():N}.test";
        var echoUpstreamUrl = $"http://host.docker.internal:{echoPort}";

        await RegisterRoute(echoDomain, echoUpstreamUrl, ct);

        try
        {
            using var proxyClient = CreateProxyClient(echoDomain);
            var response = await proxyClient.GetAsync("/", ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            Assert.Contains("x-forwarded-for", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("x-forwarded-host", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await _fixture.Client.DeleteAsync($"/api/routes/{echoDomain}", ct);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task HealthCheck_should_report_upstream_as_healthy()
    {
        var ct = TestContext.Current.CancellationToken;
        await RegisterRoute(ct: ct);

        var healthy = await WaitForHealthy(_domain, TimeSpan.FromSeconds(90), ct);
        Assert.True(healthy, "Upstream should become healthy within timeout");
    }

    [Fact(Timeout = 60_000)]
    public async Task Proxy_should_return_404_for_unknown_domain()
    {
        using var client = CreateProxyClient("unknown-domain-that-does-not-exist.test");
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task RegisterRoute(string? domain = null, string? upstreamUrl = null, CancellationToken ct = default)
    {
        domain ??= _domain;
        upstreamUrl ??= _upstreamHostUrl;

        var body = JsonSerializer.Serialize(new
        {
            domain,
            forceHttps = false,
            timeoutSeconds = 30,
            tlsMode = "selfsigned",
            firstUpstreamUrl = upstreamUrl,
        });

        var response = await _fixture.Client.PostAsync(
            "/api/routes",
            new StringContent(body, Encoding.UTF8, "application/json"),
            ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(result.GetProperty("success").GetBoolean(),
            $"Route registration failed: {result}");
    }

    private HttpClient CreateProxyClient(string? domain = null)
    {
        domain ??= _domain;

        var client = new HttpClient
        {
            BaseAddress = _fixture.ApiBaseUri,
        };
        client.DefaultRequestHeaders.Host = domain;
        return client;
    }

    private async Task<bool> WaitForHealthy(string domain, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var response = await _fixture.Client.GetAsync($"/api/health/{domain}", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var health = JsonSerializer.Deserialize<JsonElement>(json);
                    var upstreams = health.GetProperty("upstreams");

                    if (upstreams.GetArrayLength() > 0)
                    {
                        var allHealthy = upstreams.EnumerateArray()
                            .All(u => u.GetProperty("isHealthy").GetBoolean());
                        if (allHealthy)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            await Task.Delay(3000, cts.Token);
        }

        return false;
    }
}
