using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Schleusenwerk.IntegrationTests.Infrastructure;
using Xunit;

namespace Schleusenwerk.IntegrationTests.Certificates;

[Collection("Integration")]
public sealed class CertificateUploadSpec
{
    private readonly HttpClient _client;
    public CertificateUploadSpec(SchleusenwerkTestHost host) => _client = host.Client;

    [Fact(Timeout = 30_000)]
    public async Task Should_accept_pfx_certificate_upload()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("cert-pfx");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={domain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var pfxBytes = cert.Export(X509ContentType.Pfx, "testpassword");
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pfxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-pkcs12");
        content.Add(fileContent, "file", $"{domain}.pfx");
        content.Add(new StringContent("testpassword"), "password");
        var response = await _client.PostAsync($"/api/certificates/{domain}/upload", content, ct);
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_accept_pem_certificate_upload()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("cert-pem");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={domain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var certPem = cert.ExportCertificatePem();
        var keyPem = rsa.ExportRSAPrivateKeyPem();
        using var content = new MultipartFormDataContent();
        var certContent = new ByteArrayContent(Encoding.UTF8.GetBytes(certPem));
        certContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-pem-file");
        content.Add(certContent, "file", "cert.pem");
        var keyContent = new ByteArrayContent(Encoding.UTF8.GetBytes(keyPem));
        keyContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-pem-file");
        content.Add(keyContent, "keyFile", "key.pem");
        var response = await _client.PostAsync($"/api/certificates/{domain}/upload", content, ct);
        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_reject_cert_without_private_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("cert-nokey");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={domain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var certPem = cert.ExportCertificatePem();
        using var content = new MultipartFormDataContent();
        var certContent = new ByteArrayContent(Encoding.UTF8.GetBytes(certPem));
        certContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-pem-file");
        content.Add(certContent, "file", "cert.pem");
        var response = await _client.PostAsync($"/api/certificates/{domain}/upload", content, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(result.GetProperty("success").GetBoolean());
    }

    [Fact(Timeout = 30_000)]
    public async Task Should_show_uploaded_cert_details_in_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var domain = TestHelper.UniqueDomain("cert-details");
        await TestHelper.RegisterRouteAsync(_client, domain, "http://backend:8080", ct: ct);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={domain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(365));
        var pfxBytes = cert.Export(X509ContentType.Pfx, "testpassword");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pfxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-pkcs12");
        content.Add(fileContent, "file", $"{domain}.pfx");
        content.Add(new StringContent("testpassword"), "password");
        var uploadResponse = await _client.PostAsync($"/api/certificates/{domain}/upload", content, ct);
        uploadResponse.EnsureSuccessStatusCode();

        await Task.Delay(1000, ct);

        var listResponse = await _client.GetAsync("/api/certificates", ct);
        listResponse.EnsureSuccessStatusCode();
        var json = await listResponse.Content.ReadAsStringAsync(ct);
        var certs = JsonSerializer.Deserialize<JsonElement>(json);

        var found = certs.EnumerateArray().FirstOrDefault(c => c.GetProperty("domain").GetString() == domain);
        Assert.NotEqual(default, found);
        Assert.Contains(domain, found.GetProperty("subject").GetString());
    }
}
