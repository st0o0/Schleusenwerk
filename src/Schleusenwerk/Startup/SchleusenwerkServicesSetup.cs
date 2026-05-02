using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schleusenwerk.Forwarding;
using Schleusenwerk.Persistence;
using Servus.Core.Application.Startup;
using TurboHTTP;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkServicesSetup : IServiceSetupContainer
{
    public void SetupServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddTurboHttpClient();
        services.AddSingleton<RequestForwardingPipeline>();
        services.AddSingleton<HeaderManipulationFilter>();
        services.AddSingleton<WebSocketTunnel>();
        services.AddSingleton<IProxyDispatcher, ProxyDispatcher>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        services.Configure<KestrelServerOptions>(options =>
        {
            var certificate = GenerateSelfSignedCertificate();
            options.ConfigureHttpsDefaults(adapterOptions =>
            {
                adapterOptions.ServerCertificate = certificate;
            });
        });
    }

    private static X509Certificate2 GenerateSelfSignedCertificate()
    {
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest(
                "CN=localhost",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: false,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true));

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection
                    {
                        new Oid("1.3.6.1.5.5.7.3.1"), // Server Authentication
                    },
                    critical: false));

            var certificate = request.CreateSelfSigned(
                notBefore: DateTimeOffset.UtcNow,
                notAfter: DateTimeOffset.UtcNow.AddYears(1));

            return certificate;
        }
    }
}
