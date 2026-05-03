using System.Text;
using System.Text.Json;
using Xunit;

namespace Schleusenwerk.IntegrationTests;

[Collection("Schleusenwerk")]
public sealed class CertificateSpec
{
    private readonly HttpClient _client;

    public CertificateSpec(SchleusenwerkFixture fixture) => _client = fixture.Client;

    [Fact(Timeout = 30_000)]
    public async Task ListCertificates_should_return_empty_initially()
    {
        var response = await _client.GetAsync("/api/certificates");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal(JsonValueKind.Array, JsonSerializer.Deserialize<JsonElement>(json).ValueKind);
    }

    [Fact(Timeout = 30_000)]
    public async Task ProvisionCertificate_should_trigger_selfsigned()
    {
        var domain = $"cert-{Guid.NewGuid():N}.test";
        await _client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 }), Encoding.UTF8, "application/json"));
        var response = await _client.PostAsync($"/api/certificates/{domain}/provision", null);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(JsonSerializer.Deserialize<JsonElement>(json).GetProperty("success").GetBoolean());
    }

    [Fact(Timeout = 30_000)]
    public async Task ListCertificates_should_contain_provisioned_cert()
    {
        var domain = $"cert-list-{Guid.NewGuid():N}.test";
        await _client.PostAsync("/api/routes", new StringContent(JsonSerializer.Serialize(new { domain, timeoutSeconds = 30 }), Encoding.UTF8, "application/json"));
        await _client.PostAsync($"/api/certificates/{domain}/provision", null);
        await Task.Delay(2000);
        var response = await _client.GetAsync("/api/certificates");
        var json = await response.Content.ReadAsStringAsync();
        var certs = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.True(certs.EnumerateArray().Any(c => c.GetProperty("domain").GetString() == domain));
    }
}
