using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

internal sealed class LegoCertificateProvider : ILegoCertificateProvider
{
    private readonly IConfigurationStore _configStore;
    private readonly string _legoPath;
    private readonly string _webrootPath;
    private readonly ILogger<LegoCertificateProvider> _logger;

    public LegoCertificateProvider(
        IConfigurationStore configStore,
        string legoPath,
        string webrootPath,
        ILogger<LegoCertificateProvider> logger)
    {
        _configStore = configStore;
        _legoPath = legoPath;
        _webrootPath = webrootPath;
        _logger = logger;
    }

    public async Task<X509Certificate2> ProvisionAsync(DomainName domain, TlsMode mode, CancellationToken ct)
    {
        var settings = await _configStore.GetSettingsAsync(ct);
        var args = BuildArgs(settings, domain, mode, "run");

        await RunLegoAsync(args, settings, ct);

        return LoadCertificate(domain);
    }

    public async Task<X509Certificate2> RenewAsync(DomainName domain, TlsMode mode, CancellationToken ct)
    {
        var settings = await _configStore.GetSettingsAsync(ct);
        var args = BuildArgs(settings, domain, mode, "renew --days 30");

        await RunLegoAsync(args, settings, ct);

        return LoadCertificate(domain);
    }

    private string BuildArgs(ProxySettings settings, DomainName domain, TlsMode mode, string command)
    {
        var args = $"--accept-tos --email={settings.AcmeEmail} --path={_legoPath} --domains={domain.Value}";

        if (settings.Stage == AcmeStage.Staging)
        {
            args += " --server=https://acme-staging-v02.api.letsencrypt.org/directory";
        }

        args += mode switch
        {
            TlsMode.Dns => $" --dns={settings.DnsProvider}",
            _ => $" --http --http.webroot={_webrootPath}",
        };

        return $"{args} {command}";
    }

    private async Task RunLegoAsync(string args, ProxySettings settings, CancellationToken ct)
    {
        _logger.LogInformation("Running lego: {Args}", args);

        var psi = new ProcessStartInfo("lego", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start lego process");

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("lego failed (exit {Code}): {Stderr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"lego failed with exit code {process.ExitCode}: {stderr}");
        }

        _logger.LogInformation("lego completed: {Stdout}", stdout);
    }

    private X509Certificate2 LoadCertificate(DomainName domain)
    {
        var certDir = Path.Combine(_legoPath, "certificates");
        var certPath = Path.Combine(certDir, $"{domain.Value}.crt");
        var keyPath = Path.Combine(certDir, $"{domain.Value}.key");

        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            throw new FileNotFoundException($"lego did not produce certificate files at {certDir}");
        }

        var certPem = File.ReadAllText(certPath);
        var keyPem = File.ReadAllText(keyPath);

        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }
}
