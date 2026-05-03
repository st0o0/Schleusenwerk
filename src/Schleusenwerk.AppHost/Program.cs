var builder = DistributedApplication.CreateBuilder(args);

var nginx = builder.AddContainer("upstream-mock", "nginx", "alpine")
    .WithBindMount("./nginx.conf", "/etc/nginx/nginx.conf", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 80, name: "http");

var proxy = builder.AddDockerfile("proxy", "../../", "src/Schleusenwerk/Dockerfile")
    .WithHttpEndpoint(targetPort: 80, name: "http")
    .WithHttpsEndpoint(targetPort: 5000, name: "grpc")
    .WithVolume("proxy-data", "/data")
    .WaitFor(nginx);

builder.AddDockerfile("ui", "../../", "src/Schleusenwerk.UI/Dockerfile")
    .WithHttpEndpoint(targetPort: 8080, name: "http")
    .WithEnvironment("PROXY_GRPC_ENDPOINT", proxy.GetEndpoint("grpc"))
    .WaitFor(proxy);

builder.Build().Run();
