using System.Net.Security;
using Grpc.Net.Client;

namespace Schleusenwerk.UI.Services;

internal static class GrpcChannelFactory
{
    public static GrpcChannel Create(string endpoint) =>
        GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }
            }
        });
}
