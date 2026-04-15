using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Schleusenwerk.Forwarding;
using Servus.Core.Application.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkApplicationSetup : ApplicationSetupContainer<WebApplication>
{
    protected override void SetupApplication(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok("healthy"));
        app.UseWebSockets();
        app.UseProxyRequestHandler();
    }
}
