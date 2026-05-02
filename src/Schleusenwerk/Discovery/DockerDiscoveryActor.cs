using Akka.Actor;
using Akka.Event;
using Docker.DotNet;
using Docker.DotNet.Models;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;
using System.Runtime.InteropServices;
using PersistenceRemoveDomain = Schleusenwerk.Persistence.RemoveDomain;

namespace Schleusenwerk.Discovery;

/// <summary>
/// Singleton actor that discovers Docker containers with schleusenwerk.* labels
/// and registers/deregisters their routes with ConfigurationPersistenceActor.
/// Retries with exponential backoff if the Docker socket is unavailable.
/// </summary>
public sealed class DockerDiscoveryActor : ReceiveActor, IWithTimers
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _configActor;

    private IDockerClient? _client;
    private CancellationTokenSource? _monitorCts;
    private readonly Dictionary<string, (DomainName Domain, UpstreamUrl Url)> _tracked = new();

    public DockerDiscoveryActor()
    {
        _configActor = Context.GetActor<ConfigurationPersistenceActor>();

        Receive<Connect>(Handle);
        Receive<StartDiscovery>(Handle);
        Receive<ScanResult>(Handle);
        Receive<ScanFailed>(Handle);
        Receive<ContainerEvent>(Handle);
        Receive<ContainerInspected>(Handle);
        Receive<InspectFailed>(msg =>
            _log.Warning("Failed to inspect container {Id}: {Error}", msg.ContainerId[..12], msg.Error.Message));
        Receive<MonitoringEnded>(Handle);
        Receive<CheckDomainUpstreams>(Handle);
        Receive<DomainCheckResult>(Handle);
        Receive<Noop>(_ => { });
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
        try
        {
            _client?.Dispose();
            var uri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new Uri("npipe:////./pipe/docker_engine")
                : new Uri("unix:///var/run/docker.sock");

            _client = new DockerClientConfiguration(uri).CreateClient();
            _log.Info("Connected to Docker socket at {Uri}", uri);
            Self.Tell(StartDiscovery.Instance);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to connect to Docker socket: {Error} — retrying", ex.Message);
            ScheduleReconnect(msg.Attempt);
        }
    }

    private void Handle(StartDiscovery _)
    {
        _client!.Containers
            .ListContainersAsync(new ContainersListParameters { All = false })
            .ContinueWith<object>(t =>
                t.IsCompletedSuccessfully
                    ? new ScanResult(t.Result)
                    : new ScanFailed(t.Exception!))
            .PipeTo(Self);
    }

    private void Handle(ContainerEvent msg)
    {
        switch (msg.Status)
        {
            case "start":
                _client!.Containers.InspectContainerAsync(msg.ContainerId)
                    .ContinueWith<object>(t =>
                        t.IsCompletedSuccessfully
                            ? new ContainerInspected(t.Result)
                            : new InspectFailed(msg.ContainerId, t.Exception!))
                    .PipeTo(Self);
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
        RegisterContainerIfLabeled(msg.Response.ID, labels, ip);
    }

    private void Handle(MonitoringEnded msg)
    {
        if (msg.Error is not null && msg.Error is not OperationCanceledException)
        {
            _log.Warning("Docker event stream ended: {Error} — reconnecting", msg.Error.Message);
            Self.Tell(new Connect(0));
        }
    }

    private void Handle(CheckDomainUpstreams msg)
    {
        _configActor.Ask<object>(new GetDomainByName(msg.Domain), AskTimeout)
            .ContinueWith<object>(t =>
            {
                if (!t.IsCompletedSuccessfully)
                    return Noop.Instance;

                if (t.Result is DomainConfigResult result)
                    return new DomainCheckResult(msg.Domain, result);

                return Noop.Instance;
            })
            .PipeTo(Self);
    }

    private void Handle(DomainCheckResult msg)
    {
        if (msg.Config.Upstreams.Count == 0)
        {
            _configActor.Ask<object>(new PersistenceRemoveDomain(msg.Domain), AskTimeout)
                .ContinueWith(_ => Noop.Instance)
                .PipeTo(Self);

            _log.Info("Domain removed (no upstreams remain): {Domain}", msg.Domain);
        }
    }

    private void Handle(ScanResult msg)
    {
        foreach (var container in msg.Containers)
        {
            var ip = ExtractIp(container.NetworkSettings?.Networks);
            var labels = container.Labels ?? new Dictionary<string, string>();
            RegisterContainerIfLabeled(container.ID, labels, ip);
        }

        StartMonitoring();
    }

    private void Handle(ScanFailed msg)
    {
        _log.Warning("Initial container scan failed: {Error} — starting event monitor anyway", msg.Error.Message);
        StartMonitoring();
    }

    private void RegisterContainerIfLabeled(string containerId, IDictionary<string, string> labels, string? ip)
    {
        if (!labels.ContainsKey("schleusenwerk.domain"))
            return;

        if (!ContainerLabelParser.TryParse(labels, ip, out var parsed, out var error))
        {
            _log.Warning("Skipping container {Id}: {Error}", containerId[..12], error);
            return;
        }

        _tracked[containerId] = (parsed.Domain, parsed.Upstream.Url);

        var domainConfig = new DomainConfig
        {
            DomainName = parsed.Domain,
            ForceHttps = true,
        };

        // AddDomain + AddUpstream are idempotent: nack "already exists" is acceptable.
        _configActor.Tell(new AddDomain(domainConfig));
        _configActor.Tell(new AddUpstream(parsed.Domain, parsed.Upstream));

        _log.Info("Registered container {Id} → {Domain} @ {Url}", containerId[..12], parsed.Domain, parsed.Upstream.Url);
    }

    private void UnregisterContainer(string containerId)
    {
        if (!_tracked.TryGetValue(containerId, out var entry))
        {
            return;
        }

        _tracked.Remove(containerId);

        _configActor.Ask<object>(new RemoveUpstream(entry.Domain, entry.Url), AskTimeout)
            .ContinueWith<object>(t =>
                t is { IsCompletedSuccessfully: true, Result: ConfigurationCommandAck }
                    ? new CheckDomainUpstreams(entry.Domain)
                    : Noop.Instance)
            .PipeTo(Self);

        _log.Info("Unregistered container {Id} upstream {Url}", containerId[..12], entry.Url);
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
            .ContinueWith<object>(t =>
                !t.IsCanceled
                    ? new MonitoringEnded(t.Exception?.InnerException ?? t.Exception)
                    : Noop.Instance)
            .PipeTo(Self);
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
    private sealed record CheckDomainUpstreams(DomainName Domain);
    private sealed record DomainCheckResult(DomainName Domain, DomainConfigResult Config);
    private sealed record Noop
    {
        public static Noop Instance { get; } = new();
    }
}
