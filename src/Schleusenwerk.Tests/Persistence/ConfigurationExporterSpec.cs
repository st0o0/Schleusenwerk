using System.Text.Json;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class ConfigurationExporterSpec
{
    private static ConfigurationSnapshot CreateSnapshot(
        IReadOnlyList<DomainConfig>? domains = null,
        IReadOnlyDictionary<string, IReadOnlyList<UpstreamTarget>>? upstreams = null,
        IReadOnlyDictionary<string, CertificateInfo>? certificates = null,
        ProxySettings? settings = null)
    {
        return new ConfigurationSnapshot
        {
            Domains = domains ?? [],
            Upstreams = upstreams ?? new Dictionary<string, IReadOnlyList<UpstreamTarget>>(),
            Certificates = certificates ?? new Dictionary<string, CertificateInfo>(),
            Settings = settings ?? ProxySettings.Default,
        };
    }

    private static DomainConfig CreateDomain(string name, bool forceHttps = false)
    {
        return new DomainConfig
        {
            DomainName = DomainName.Parse(name),
            ForceHttps = forceHttps,
        };
    }

    private static CertificateInfo CreateCertificate()
    {
        return new CertificateInfo
        {
            Thumbprint = "ABC123",
            NotBefore = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            NotAfter = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Issuer = "Let's Encrypt",
            IsSelfSigned = false,
        };
    }

    [Fact(Timeout = 5000)]
    public void ToExportDocument_should_export_empty_snapshot()
    {
        var snapshot = CreateSnapshot();

        var doc = ConfigurationExporter.ToExportDocument(snapshot);

        Assert.Empty(doc.Domains);
        Assert.Equal(ProxySettings.Default.DefaultRequestTimeout.TotalSeconds, doc.Settings.DefaultRequestTimeoutSeconds);
    }

    [Fact(Timeout = 5000)]
    public void ToExportDocument_should_export_domains_with_upstreams()
    {
        var domain = CreateDomain("example.com", forceHttps: true);
        var upstream = UpstreamTarget.Create("http://localhost:8080", weight: 2, maxConnections: 50);
        var snapshot = CreateSnapshot(
            domains: [domain],
            upstreams: new Dictionary<string, IReadOnlyList<UpstreamTarget>>
            {
                ["example.com"] = [upstream],
            });

        var doc = ConfigurationExporter.ToExportDocument(snapshot);

        Assert.Single(doc.Domains);
        var entry = doc.Domains[0];
        Assert.Equal("example.com", entry.DomainName);
        Assert.True(entry.ForceHttps);
        Assert.Single(entry.Upstreams);
        Assert.Equal("http://localhost:8080/", entry.Upstreams[0].Url);
        Assert.Equal(2, entry.Upstreams[0].Weight);
        Assert.Equal(50, entry.Upstreams[0].MaxConnections);
    }

    [Fact(Timeout = 5000)]
    public void ToExportDocument_should_include_certificate_by_default()
    {
        var domain = CreateDomain("example.com");
        var certInfo = CreateCertificate();
        var snapshot = CreateSnapshot(
            domains: [domain],
            certificates: new Dictionary<string, CertificateInfo>
            {
                ["example.com"] = certInfo,
            });

        var doc = ConfigurationExporter.ToExportDocument(snapshot);

        var exportedCert = doc.Domains[0].Certificate;
        Assert.NotNull(exportedCert);
        Assert.Equal("ABC123", exportedCert.Thumbprint);
        Assert.Equal("Let's Encrypt", exportedCert.Issuer);
    }

    [Fact(Timeout = 5000)]
    public void ToExportDocument_should_exclude_certificate_when_sensitive_data_excluded()
    {
        var domain = CreateDomain("example.com");
        var cert = CreateCertificate();
        var snapshot = CreateSnapshot(
            domains: [domain],
            certificates: new Dictionary<string, CertificateInfo>
            {
                ["example.com"] = cert,
            });
        var options = new ConfigurationExportOptions { ExcludeSensitiveData = true };

        var doc = ConfigurationExporter.ToExportDocument(snapshot, options);

        Assert.Null(doc.Domains[0].Certificate);
    }

    [Fact(Timeout = 5000)]
    public void ToExportDocument_should_export_settings()
    {
        var settings = new ProxySettings
        {
            DefaultRequestTimeout = TimeSpan.FromSeconds(60),
            MaxConnectionsPerUpstream = 200,
            ForceHttpsGlobally = true,
            SnapshotInterval = 50,
        };
        var snapshot = CreateSnapshot(settings: settings);

        var doc = ConfigurationExporter.ToExportDocument(snapshot);

        Assert.Equal(60, doc.Settings.DefaultRequestTimeoutSeconds);
        Assert.Equal(200, doc.Settings.MaxConnectionsPerUpstream);
        Assert.True(doc.Settings.ForceHttpsGlobally);
        Assert.Equal(50, doc.Settings.SnapshotInterval);
    }

    [Fact(Timeout = 5000)]
    public void ToJson_should_produce_valid_json()
    {
        var domain = CreateDomain("example.com");
        var snapshot = CreateSnapshot(domains: [domain]);

        var json = ConfigurationExporter.ToJson(snapshot);

        Assert.NotEmpty(json);
        var parsed = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.True(parsed.RootElement.TryGetProperty("domains", out _));
        Assert.True(parsed.RootElement.TryGetProperty("settings", out _));
    }

    [Fact(Timeout = 5000)]
    public void ToJson_should_omit_null_certificate()
    {
        var domain = CreateDomain("example.com");
        var snapshot = CreateSnapshot(domains: [domain]);

        var json = ConfigurationExporter.ToJson(snapshot);

        Assert.DoesNotContain("certificate", json);
    }

    [Fact(Timeout = 5000)]
    public void Roundtrip_should_preserve_domains_and_upstreams()
    {
        var domain = CreateDomain("api.example.com", forceHttps: true);
        var upstream1 = UpstreamTarget.Create("http://backend-1:3000", weight: 3);
        var upstream2 = UpstreamTarget.Create("https://backend-2:3001", maxConnections: 25);
        var snapshot = CreateSnapshot(
            domains: [domain],
            upstreams: new Dictionary<string, IReadOnlyList<UpstreamTarget>>
            {
                ["api.example.com"] = [upstream1, upstream2],
            });

        var json = ConfigurationExporter.ToJson(snapshot);
        var doc = ConfigurationExporter.FromJson(json);
        Assert.NotNull(doc);
        var restored = ConfigurationExporter.ToSnapshot(doc);

        Assert.Single(restored.Domains);
        Assert.Equal("api.example.com", restored.Domains[0].DomainName.Value);
        Assert.True(restored.Domains[0].ForceHttps);
        Assert.Equal(2, restored.Upstreams["api.example.com"].Count);
        Assert.Equal(3, restored.Upstreams["api.example.com"][0].Weight);
        Assert.Equal(25, restored.Upstreams["api.example.com"][1].MaxConnections);
    }

    [Fact(Timeout = 5000)]
    public void Roundtrip_should_preserve_certificates()
    {
        var domain = CreateDomain("secure.example.com");
        var cert = CreateCertificate();
        var snapshot = CreateSnapshot(
            domains: [domain],
            certificates: new Dictionary<string, CertificateInfo>
            {
                ["secure.example.com"] = cert,
            });

        var json = ConfigurationExporter.ToJson(snapshot);
        var doc = ConfigurationExporter.FromJson(json);
        Assert.NotNull(doc);
        var restored = ConfigurationExporter.ToSnapshot(doc);

        Assert.Single(restored.Certificates);
        Assert.Equal("ABC123", restored.Certificates["secure.example.com"].Thumbprint);
        Assert.Equal("Let's Encrypt", restored.Certificates["secure.example.com"].Issuer);
        Assert.False(restored.Certificates["secure.example.com"].IsSelfSigned);
    }

    [Fact(Timeout = 5000)]
    public void Roundtrip_should_preserve_settings()
    {
        var settings = new ProxySettings
        {
            DefaultRequestTimeout = TimeSpan.FromSeconds(45),
            MaxConnectionsPerUpstream = 75,
            ForceHttpsGlobally = true,
            SnapshotInterval = 250,
        };
        var snapshot = CreateSnapshot(settings: settings);

        var json = ConfigurationExporter.ToJson(snapshot);
        var doc = ConfigurationExporter.FromJson(json);
        Assert.NotNull(doc);
        var restored = ConfigurationExporter.ToSnapshot(doc);

        Assert.Equal(TimeSpan.FromSeconds(45), restored.Settings.DefaultRequestTimeout);
        Assert.Equal(75, restored.Settings.MaxConnectionsPerUpstream);
        Assert.True(restored.Settings.ForceHttpsGlobally);
        Assert.Equal(250, restored.Settings.SnapshotInterval);
    }

    [Fact(Timeout = 5000)]
    public void Roundtrip_should_preserve_multiple_domains()
    {
        var domain1 = CreateDomain("a.com");
        var domain2 = new DomainConfig
        {
            DomainName = DomainName.Parse("b.com"),
            HttpRedirect = RedirectMode.PermanentRedirect,
            PreserveHostHeader = false,
            RequestTimeout = TimeSpan.FromSeconds(10),
        };
        var snapshot = CreateSnapshot(domains: [domain1, domain2]);

        var json = ConfigurationExporter.ToJson(snapshot);
        var doc = ConfigurationExporter.FromJson(json);
        Assert.NotNull(doc);
        var restored = ConfigurationExporter.ToSnapshot(doc);

        Assert.Equal(2, restored.Domains.Count);

        var restoredB = restored.Domains.First(d => d.DomainName.Value == "b.com");
        Assert.Equal(RedirectMode.PermanentRedirect, restoredB.HttpRedirect);
        Assert.False(restoredB.PreserveHostHeader);
        Assert.Equal(TimeSpan.FromSeconds(10), restoredB.RequestTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Roundtrip_without_sensitive_data_should_lose_certificates_only()
    {
        var domain = CreateDomain("example.com");
        var cert = CreateCertificate();
        var upstream = UpstreamTarget.Create("http://localhost:8080");
        var snapshot = CreateSnapshot(
            domains: [domain],
            upstreams: new Dictionary<string, IReadOnlyList<UpstreamTarget>>
            {
                ["example.com"] = [upstream],
            },
            certificates: new Dictionary<string, CertificateInfo>
            {
                ["example.com"] = cert,
            });
        var options = new ConfigurationExportOptions { ExcludeSensitiveData = true };

        var json = ConfigurationExporter.ToJson(snapshot, options);
        var doc = ConfigurationExporter.FromJson(json);
        Assert.NotNull(doc);
        var restored = ConfigurationExporter.ToSnapshot(doc);

        Assert.Single(restored.Domains);
        Assert.Single(restored.Upstreams["example.com"]);
        Assert.Empty(restored.Certificates);
    }

    [Fact(Timeout = 5000)]
    public void ToExportDocument_should_handle_domain_without_upstreams()
    {
        var domain = CreateDomain("orphan.com");
        var snapshot = CreateSnapshot(domains: [domain]);

        var doc = ConfigurationExporter.ToExportDocument(snapshot);

        Assert.Single(doc.Domains);
        Assert.Empty(doc.Domains[0].Upstreams);
    }
}
