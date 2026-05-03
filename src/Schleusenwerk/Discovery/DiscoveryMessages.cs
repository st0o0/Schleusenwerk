using Schleusenwerk.Routing;

namespace Schleusenwerk.Discovery;

public sealed record GetDiscoveredContainers
{
    public static readonly GetDiscoveredContainers Instance = new();
}

public sealed record TrackedContainer(
    string ContainerId,
    string Name,
    string Image,
    string Status,
    IReadOnlyDictionary<string, string> Labels,
    DomainName? AssignedDomain,
    UpstreamUrl? AssignedUrl);

public sealed record DiscoveredContainersResult(IReadOnlyList<TrackedContainer> Containers);
