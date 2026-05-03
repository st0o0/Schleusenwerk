var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

builder.AddDockerfile("proxy", repoRoot, "src/Schleusenwerk/Dockerfile")
    .WithEndpoint("api", e =>
    {
        e.TargetPort = 5000;
        e.UriScheme = "http";
        e.IsProxied = false;
        e.Port = 15000;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://+:5000")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Akka__Persistence__ConnectionString", "Data Source=/tmp/schleusenwerk.db")
    .WithEnvironment("Certificates__Path", "/tmp/certs");

builder.Build().Run();

static string FindRepoRoot(string startDir)
{
    var dir = startDir;
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git")))
        {
            return dir;
        }

        dir = Directory.GetParent(dir)?.FullName;
    }

    throw new InvalidOperationException("Could not find repository root (.git directory)");
}
