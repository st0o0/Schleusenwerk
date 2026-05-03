var builder = DistributedApplication.CreateBuilder(args);

var nginx = builder.AddContainer("upstream-mock", "nginx", "alpine")
    .WithBindMount("./nginx.conf", "/etc/nginx/nginx.conf", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 80, name: "http");

var proxy = builder.AddProject<Projects.Schleusenwerk>("proxy")
    .WithHttpEndpoint(name: "http")
    .WithHttpsEndpoint(name: "grpc")
    .WaitFor(nginx);

builder.AddProject<Projects.Schleusenwerk_UI>("ui")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("PROXY_GRPC_ENDPOINT", proxy.GetEndpoint("grpc"))
    .WaitFor(proxy);

builder.Build().Run();
