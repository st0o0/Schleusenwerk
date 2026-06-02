using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Discovery;

[Collection("DockerDiscovery")]
public sealed class DockerAutoRegistrationSpec
{
    private readonly DockerDiscoveryTestHost _host;

    public DockerAutoRegistrationSpec(DockerDiscoveryTestHost host) => _host = host;

    [Fact(Timeout = 120_000)]
    public async Task Should_auto_register_route_for_labeled_container()
    {
        DockerAvailableGuard.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var domain = $"auto-{Guid.NewGuid():N}.test";

        var container = new ContainerBuilder()
            .WithImage("nginx:alpine")
            .WithLabel("schleusenwerk.domain", domain)
            .WithLabel("schleusenwerk.port", "80")
            .WithLabel("schleusenwerk.tls", "selfsigned")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        await container.StartAsync(ct);
        try
        {
            var found = await WaitForRouteAsync(domain, ct);
            Assert.True(found, $"Route for {domain} was not auto-registered within timeout");
        }
        finally
        {
            await container.StopAsync(ct);
            await container.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Should_show_labeled_container_in_discovery_api()
    {
        DockerAvailableGuard.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var domain = $"disc-api-{Guid.NewGuid():N}.test";

        var container = new ContainerBuilder()
            .WithImage("nginx:alpine")
            .WithLabel("schleusenwerk.domain", domain)
            .WithLabel("schleusenwerk.port", "80")
            .WithLabel("schleusenwerk.tls", "selfsigned")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        await container.StartAsync(ct);
        try
        {
            await WaitForRouteAsync(domain, ct);
            var response = await _host.Client.GetAsync("/api/discovery/containers", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var containers = JsonSerializer.Deserialize<JsonElement>(json);
            Assert.Contains(containers.EnumerateArray(),
                c => c.GetProperty("assignedDomain").GetString() == domain);
        }
        finally
        {
            await container.StopAsync(ct);
            await container.DisposeAsync();
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Should_deregister_upstream_when_container_stops()
    {
        DockerAvailableGuard.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var domain = $"dereg-{Guid.NewGuid():N}.test";

        var container = new ContainerBuilder()
            .WithImage("nginx:alpine")
            .WithLabel("schleusenwerk.domain", domain)
            .WithLabel("schleusenwerk.port", "80")
            .WithLabel("schleusenwerk.tls", "selfsigned")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        await container.StartAsync(ct);
        var found = await WaitForRouteAsync(domain, ct);
        Assert.True(found, $"Route for {domain} was not auto-registered");

        await container.StopAsync(ct);
        await container.DisposeAsync();

        await Task.Delay(5000, ct);

        using var proxyClient = TestHelper.CreateProxyClient(_host.BaseUri, domain);
        var response = await proxyClient.GetAsync("/", ct);
        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.BadGateway
                or System.Net.HttpStatusCode.NotFound,
            $"Expected 502 or 404 after container stopped, got {response.StatusCode}");
    }

    [Fact(Timeout = 120_000)]
    public async Task Should_ignore_container_without_schleusenwerk_labels()
    {
        DockerAvailableGuard.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var container = new ContainerBuilder()
            .WithImage("nginx:alpine")
            .WithLabel("some.other.label", "value")
            .WithPortBinding(80, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(80)))
            .Build();

        await container.StartAsync(ct);
        try
        {
            await Task.Delay(5000, ct);

            var routesResponse = await _host.Client.GetAsync("/api/routes", ct);
            routesResponse.EnsureSuccessStatusCode();
            var routesJson = await routesResponse.Content.ReadAsStringAsync(ct);
            var routes = JsonSerializer.Deserialize<JsonElement>(routesJson);

            Assert.DoesNotContain(routes.EnumerateArray(),
                r => r.GetProperty("domain").GetString() == "value");
        }
        finally
        {
            await container.StopAsync(ct);
            await container.DisposeAsync();
        }
    }

    private async Task<bool> WaitForRouteAsync(string domain, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                var response = await _host.Client.GetAsync("/api/routes", ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    var routes = JsonSerializer.Deserialize<JsonElement>(json);
                    if (routes.EnumerateArray().Any(r =>
                        r.GetProperty("domain").GetString() == domain))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(2000, ct);
        }

        return false;
    }
}
