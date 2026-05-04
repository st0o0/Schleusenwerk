using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Discovery;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/discovery")]
public sealed class DiscoveryController : ControllerBase
{
    private readonly IActorRef _discoveryActor;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public DiscoveryController(IReadOnlyActorRegistry registry)
    {
        _discoveryActor = registry.Get<DockerDiscoveryActor>();
    }

    [HttpGet("containers")]
    public async Task<ActionResult<IReadOnlyList<DiscoveredContainerDto>>> ListContainers(CancellationToken ct)
    {
        var result = await _discoveryActor.Ask<DiscoveredContainersResult>(
            GetDiscoveredContainers.Instance, _timeout, ct);

        var dtos = result.Containers.Select(c => new DiscoveredContainerDto(
            Name: c.Name,
            Image: c.Image,
            Status: c.Status,
            Labels: c.Labels,
            AssignedDomain: c.AssignedDomain?.Value,
            ConflictReason: null)).ToList();

        return Ok(dtos);
    }
}
