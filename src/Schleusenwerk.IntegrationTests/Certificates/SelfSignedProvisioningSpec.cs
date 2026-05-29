using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Certificates;

[Collection("Integration")]
public sealed class SelfSignedProvisioningSpec
{
    private readonly HttpClient _client;
    public SelfSignedProvisioningSpec(SchleusenwerkTestHost host) => _client = host.Client;

    [Fact(Timeout = 30_000)]
    public async Task Should_provision_selfsigned_cert_for_new_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("cert-prov");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        var response = await _client.PostAsync($"/api/certificates/{domain}/provision", null, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        Assert.True(JsonSerializer.Deserialize<JsonElement>(json).GetProperty("success").GetBoolean());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_return_cert_details_via_api()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("cert-detail");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        await _client.PostAsync($"/api/certificates/{domain}/provision", null, ct);
        await Task.Delay(2000, ct);
        var response = await _client.GetAsync("/api/certificates", ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var certs = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Contains(certs.EnumerateArray(), c => c.GetProperty("domain").GetString() == domain);
    }
}
