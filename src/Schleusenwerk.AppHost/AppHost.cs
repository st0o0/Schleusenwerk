var builder = DistributedApplication.CreateBuilder(args);

var src = Path.Combine(builder.AppHostDirectory, "..");
var root = Path.Combine(src, "..");

var sample = builder.AddContainer("sample", "nginxdemos/hello", "latest")
    .WithHttpEndpoint(targetPort: 80, name: "http");

var proxy = builder.AddDockerfile("proxy", root, Path.Combine(src, "Schleusenwerk", "Dockerfile"))
    .WithHttpEndpoint(port: 1312, targetPort: 80, name: "http")
    .WithHttpEndpoint(port: 5000, targetPort: 5000, name: "management")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", "http://+:80;http://+:5000")
    .WithEnvironment("STAGE", "local")
    .WithEnvironment("Docker__Enabled", "false")
    .WithEnvironment("Cors__AllowedOrigins", "http://localhost:3000,http://localhost:5173")
    .WithEnvironment("OTEL_SERVICE_NAME", "schleusenwerk")
    .WithOtlpExporter()
    .WithEnvironment("DOMAINS", "sample.localhost -> http://sample:80")
    .WithVolume("schleusenwerk-data", "/data")
    .WithVolume("schleusenwerk-certs", "/certs")
    .WaitFor(sample);

var web = builder.AddDockerfile("web", Path.Combine(src, "Schleusenwerk.Web"), "Dockerfile.prod")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "ui")
    .WaitFor(proxy);

await builder.Build().RunAsync();
