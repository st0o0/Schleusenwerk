using Akka.Actor;
using Akka.Event;
using Docker.DotNet;
using Docker.DotNet.Models;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;
using System.Runtime.InteropServices;

namespace Schleusenwerk.Discovery;

/// <summary>
/// Singleton actor that discovers Docker containers with schleusenwerk.* labels
/// and registers/deregisters their routes via the DomainEntityActor shard region.
/// Retries with exponential backoff if the Docker socket is unavailable.
/// </summary>
public sealed class DockerDiscoveryActor : ReceiveActor, IWithTimers
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _domainRegion;

    private DockerClient? _client;
    private CancellationTokenSource? _monitorCts;
    private readonly Dictionary<string, TrackedContainer> _tracked = new();

    public DockerDiscoveryActor()
    {
        _domainRegion = Context.GetActor<DomainEntityActor>();

        Receive<Connect>(Handle);
        Receive<StartDiscovery>(Handle);
        Receive<ScanResult>(Handle);
        Receive<ScanFailed>(Handle);
        Receive<ContainerEvent>(Handle);
        Receive<ContainerInspected>(Handle);
        Receive<InspectFailed>(msg =>
            _log.Warning("Failed to inspect container {Id}: {Error}", msg.ContainerId[..12], msg.Error.Message));
        Receive<MonitoringEnded>(Handle);
        Receive<Noop>(_ => { });
        Receive<GetDiscoveredContainers>(_ =>
            Sender.Tell(new DiscoveredContainersResult(_tracked.Values.ToList())));
    }

    protected override void PreStart()
    {
        base.PreStart();
        Self.Tell(new Connect(0));
    }

    protected override void PostStop()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _client?.Dispose();
        base.PostStop();
    }

    private void Handle(Connect msg)
    {
        _client?.Dispose();
        var uri = ResolveContainerEngineUri();

        if (uri is null)
        {
            _log.Info("No container engine socket found — discovery disabled");
            return;
        }

        try
        {
            _client = new DockerClientConfiguration(uri).CreateClient();
            _log.Info("Connected to container engine at {Uri}", uri);
            Self.Tell(StartDiscovery.Instance);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to connect to container engine at {Uri}: {Error} — retrying", uri, ex.Message);
            ScheduleReconnect(msg.Attempt);
        }
    }

    private static Uri? ResolveContainerEngineUri()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
            return new Uri(dockerHost);

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ResolveWindowsPipeUri()
            : ResolveUnixSocketUri();
    }

    private static Uri? ResolveWindowsPipeUri()
    {
        string[] candidates = ["docker_engine", "podman-machine-default", "podman"];
        HashSet<string> pipes;
        try
        {
            pipes = new HashSet<string>(
                Directory.GetFiles(@"\\.\pipe\").Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }

        foreach (var name in candidates)
        {
            if (pipes.Contains(name))
                return new Uri($"npipe://./pipe/{name}");
        }

        return null;
    }

    private static Uri? ResolveUnixSocketUri()
    {
        var candidates = new List<string>
        {
            "/var/run/docker.sock",
            "/run/podman/podman.sock",
        };

        // Rootless Podman uses XDG_RUNTIME_DIR (e.g. /run/user/1000/podman/podman.sock).
        var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntimeDir))
            candidates.Add(Path.Combine(xdgRuntimeDir, "podman", "podman.sock"));

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return new Uri($"unix://{path}");
        }

        return null;
    }

    private void Handle(StartDiscovery _)
    {
        _client!.Containers
            .ListContainersAsync(new ContainersListParameters { All = false })
            .PipeTo(Self,
                success: result => new ScanResult(result),
                failure: ex => new ScanFailed(ex));
    }

    private void Handle(ContainerEvent msg)
    {
        switch (msg.Status)
        {
            case "start":
                _client!.Containers.InspectContainerAsync(msg.ContainerId)
                    .PipeTo(Self,
                        success: result => new ContainerInspected(result),
                        failure: ex => new InspectFailed(msg.ContainerId, ex));
                break;

            case "stop":
            case "die":
            case "kill":
                UnregisterContainer(msg.ContainerId);
                break;
        }
    }

    private void Handle(ContainerInspected msg)
    {
        var ip = ExtractIp(msg.Response.NetworkSettings.Networks);
        var labels = msg.Response.Config.Labels ?? new Dictionary<string, string>();
        RegisterContainerIfLabeled(
            msg.Response.ID,
            msg.Response.Name?.TrimStart('/') ?? msg.Response.ID[..12],
            msg.Response.Config.Image ?? "",
            msg.Response.State?.Status ?? "",
            labels,
            ip);
    }

    private void Handle(MonitoringEnded msg)
    {
        if (msg.Error is not null && msg.Error is not OperationCanceledException)
        {
            _log.Warning("Docker event stream ended: {Error} — reconnecting", msg.Error.Message);
            Self.Tell(new Connect(0));
        }
    }


    private void Handle(ScanResult msg)
    {
        foreach (var container in msg.Containers)
        {
            var ip = ExtractIp(container.NetworkSettings?.Networks);
            var labels = container.Labels ?? new Dictionary<string, string>();
            RegisterContainerIfLabeled(
                container.ID,
                container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12],
                container.Image ?? "",
                container.State ?? "",
                labels,
                ip);
        }
        StartMonitoring();
    }

    private void Handle(ScanFailed msg)
    {
        _log.Warning("Initial container scan failed: {Error} — starting event monitor anyway", msg.Error.Message);
        StartMonitoring();
    }

    private void RegisterContainerIfLabeled(
        string containerId, string name, string image, string status,
        IDictionary<string, string> labels, string? ip)
    {
        if (!labels.ContainsKey("schleusenwerk.domain"))
        {
            _tracked[containerId] = new TrackedContainer(
                containerId, name, image, status,
                new Dictionary<string, string>(labels), null, null);
            return;
        }

        if (!ContainerLabelParser.TryParse(labels, ip, out var parsed, out var error))
        {
            _log.Warning("Skipping container {Id}: {Error}", containerId[..12], error);
            _tracked[containerId] = new TrackedContainer(
                containerId, name, image, status,
                new Dictionary<string, string>(labels), null, null);
            return;
        }

        _tracked[containerId] = new TrackedContainer(
            containerId, name, image, status,
            new Dictionary<string, string>(labels), parsed.Domain, parsed.Upstream.Url);

        var domainConfig = new DomainConfig
        {
            DomainName = parsed.Domain,
            ForceHttps = true,
        };

        _domainRegion.Tell(new AddDomain(domainConfig));
        _domainRegion.Tell(new AddUpstream(parsed.Domain, parsed.Upstream));

        _log.Info("Registered container {Id} → {Domain} @ {Url}", containerId[..12], parsed.Domain, parsed.Upstream.Url);
    }

    private void UnregisterContainer(string containerId)
    {
        if (!_tracked.Remove(containerId, out var entry))
        {
            return;
        }

        if (entry.AssignedDomain is not null && entry.AssignedUrl is not null)
        {
            _domainRegion.Tell(new RemoveUpstream(entry.AssignedDomain.Value, entry.AssignedUrl.Value));
            _log.Info("Unregistered container {Id} upstream {Url}", containerId[..12], entry.AssignedUrl);
        }
    }

    private void StartMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        var progress = new Progress<Message>(msg =>
        {
            if (msg.Type == "container")
            {
                Self.Tell(new ContainerEvent(msg.ID, msg.Status));
            }
        });

        _client!.System.MonitorEventsAsync(new ContainerEventsParameters(), progress, token)
            .PipeTo(Self,
                success: () => new MonitoringEnded(null),
                failure: ex => ex is OperationCanceledException
                    ? Noop.Instance
                    : new MonitoringEnded(ex));
    }

    private void ScheduleReconnect(int attempt)
    {
        var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), MaxRetryDelay.TotalSeconds));
        _log.Warning("Will retry Docker connection in {Delay}s (attempt {Attempt})", delay.TotalSeconds, attempt + 1);
        Timers.StartSingleTimer("reconnect", new Connect(attempt + 1), delay);
    }

    private static string? ExtractIp(IDictionary<string, EndpointSettings>? networks)
    {
        if (networks is null)
            return null;

        foreach (var network in networks.Values)
        {
            if (!string.IsNullOrWhiteSpace(network.IPAddress))
                return network.IPAddress;
        }

        return null;
    }

    // --- Internal messages ---

    private sealed record Connect(int Attempt);
    private sealed record StartDiscovery
    {
        public static StartDiscovery Instance { get; } = new();
    }
    private sealed record ContainerEvent(string ContainerId, string Status);
    private sealed record ContainerInspected(ContainerInspectResponse Response);
    private sealed record InspectFailed(string ContainerId, Exception Error);
    private sealed record MonitoringEnded(Exception? Error);
    private sealed record ScanResult(IList<ContainerListResponse> Containers);
    private sealed record ScanFailed(Exception Error);
    private sealed record Noop
    {
        public static Noop Instance { get; } = new();
    }
}
