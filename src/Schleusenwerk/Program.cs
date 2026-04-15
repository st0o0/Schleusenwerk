using Akka.Hosting;
using Schleusenwerk.Infrastructure.Forwarding;
using TurboHTTP;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboHttpClient();
builder.Services.AddSingleton<RequestForwardingPipeline>();

builder.Services.AddAkka("schleusenwerk", (configurationBuilder, provider) =>
{
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
