var builder = DistributedApplication.CreateBuilder(args);

var sample = builder.AddContainer("sample", "nginxdemos/hello", "latest")
    .WithHttpEndpoint(targetPort: 8080, name: "http");

var proxy = builder.AddContainer("proxy", "ghcr.io/st0o0/schleusenwerk", "edge")
    .WithHttpEndpoint(port: 1312, targetPort: 80, name: "http")
    .WithHttpEndpoint(port: 5000, targetPort: 5000, name: "management")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", "http://+:80;http://+:5000")
    .WithEnvironment("STAGE", "local")
    .WithEnvironment("Docker__Enabled", "false")
    .WithEnvironment("Cors__AllowedOrigins", "http://localhost:3000,http://localhost:5173")
    .WithEnvironment("OTEL_SERVICE_NAME", "schleusenwerk")
    .WithOtlpExporter()
    .WithEnvironment(ctx =>
    {
        var sampleEndpoint = sample.GetEndpoint("http");
        ctx.EnvironmentVariables["DOMAINS"] = ReferenceExpression.Create(
            $"sample.localhost -> {sampleEndpoint}");
    })
    .WithVolume("schleusenwerk-data", "/data")
    .WithVolume("schleusenwerk-certs", "/certs")
    .WaitFor(sample);

var web = builder.AddContainer("web", "ghcr.io/st0o0/schleusenwerk-web", "edge")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "ui")
    .WaitFor(proxy);

await builder.Build().RunAsync();
