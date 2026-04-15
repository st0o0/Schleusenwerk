using Akka.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("schleusenwerk", (configurationBuilder, provider) =>
{
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
