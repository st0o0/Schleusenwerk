using Schleusenwerk.Forwarding;
using Schleusenwerk.Persistence;
using Schleusenwerk.RateLimiting;
using Servus.Core.Application.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkApplicationSetup : ApplicationSetupContainer<WebApplication>
{
    protected override void SetupApplication(WebApplication app)
    {
        app.MapGet("/health", async (IConfigurationStore store, CancellationToken ct) =>
        {
            try
            {
                await store.GetSettingsAsync(ct);
                return Results.Ok("healthy");
            }
            catch
            {
                return Results.StatusCode(503);
            }
        });

        app.MapGet("/.well-known/acme-challenge/{token}", (string token, IConfiguration config) =>
        {
            var webrootPath = config["Lego:WebrootPath"] ?? "/tmp/acme-webroot";
            var filePath = Path.Combine(webrootPath, ".well-known", "acme-challenge", token);
            if (!File.Exists(filePath))
            {
                return Results.NotFound();
            }

            return Results.Text(File.ReadAllText(filePath));
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

