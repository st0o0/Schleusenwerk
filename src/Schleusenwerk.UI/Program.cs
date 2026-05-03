using MudBlazor.Services;
using Schleusenwerk.UI.Hubs;
using Schleusenwerk.UI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();
builder.Services.AddMudServices();

var grpcEndpoint = builder.Configuration["PROXY_GRPC_ENDPOINT"] ?? "http://localhost:5000";
builder.Services.AddSingleton(GrpcChannelFactory.Create(grpcEndpoint));

builder.Services.AddSingleton<IRouteClient, RouteClient>();
builder.Services.AddSingleton<ICertificateClient, CertificateClient>();
builder.Services.AddSingleton<IHealthClient, HealthClient>();
builder.Services.AddHostedService<EventStreamBackgroundService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapHub<ProxyEventHub>("/hubs/events");
app.MapRazorComponents<Schleusenwerk.UI.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
