using Schleusenwerk.Certificates;
using Schleusenwerk.Forwarding;
using Schleusenwerk.RateLimiting;
using Servus.Core.Application.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkApplicationSetup : ApplicationSetupContainer<WebApplication>
{
    protected override void SetupApplication(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok("healthy"));

        app.MapGet("/.well-known/acme-challenge/{token}", (string token, AcmeChallengeStore store) =>
        {
            var keyAuthz = store.GetChallenge(token);
            return keyAuthz is not null ? Results.Text(keyAuthz) : Results.NotFound();
        });

        app.UseCors();
        app.MapControllers();
        app.MapHub<Hubs.ProxyEventHub>("/hubs/events");

        if (!app.Environment.IsDevelopment())
        {
            app.Use(HttpsRedirectionMiddleware);
        }
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

