using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Api;

[Collection("Integration")]
public sealed class CertificateApiSpec
{
    private readonly HttpClient _client;
    public CertificateApiSpec(SchleusenwerkTestHost host) => _client = host.Client;

    [Fact(Timeout = 30_000)]
    public async Task Should_list_certificates()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/certificates", ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Array, JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct)).ValueKind);
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_trigger_provisioning()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("cert-trigger");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        var response = await _client.PostAsync($"/api/certificates/{domain}/provision", null, ct);
        response.EnsureSuccessStatusCode();
        Assert.True(JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct)).GetProperty("success").GetBoolean());
    }
}
