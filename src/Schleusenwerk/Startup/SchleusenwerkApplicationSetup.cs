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

        app.Use(HttpsRedirectionMiddleware);
        app.UseWebSockets();

        app.MapFallback(async (HttpContext ctx, IProxyDispatcher dispatcher, CancellationToken ct) =>
            await dispatcher.HandleAsync(ctx, ct));
    }

    private static async Task HttpsRedirectionMiddleware(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/.well-known/acme-challenge"))
        {
            await next(context);
            return;
        }

        if (context.Request.Scheme == "http")
        {
            var httpsUrl = $"https://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
            context.Response.Headers.Location = httpsUrl;
            return;
        }

        await next(context);
    }
}

