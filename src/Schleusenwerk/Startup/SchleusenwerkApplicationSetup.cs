using Schleusenwerk.Forwarding;
using Schleusenwerk.RateLimiting;
using Servus.Core.Application.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkApplicationSetup : ApplicationSetupContainer<WebApplication>
{
    protected override void SetupApplication(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok("healthy"));

        app.MapGrpcService<Grpc.RouteServiceImpl>();
        app.MapGrpcService<Grpc.CertificateServiceImpl>();
        app.MapGrpcService<Grpc.HealthServiceImpl>();
        app.MapGrpcService<Grpc.EventServiceImpl>();

        app.Use(HttpsRedirectionMiddleware);
        app.UseWebSockets();
        app.UseRateLimiter();

        app.MapFallback(async (HttpContext ctx, IProxyDispatcher dispatcher, CancellationToken ct) =>
            await dispatcher.HandleAsync(ctx, ct))
            .RequireRateLimiting(DomainRateLimitPolicy.PolicyName);
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

