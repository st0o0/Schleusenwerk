# MVP: Message Routing Fix & Basic TLS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the broken message flow between actors so the proxy actually routes traffic end-to-end, then add per-domain SNI certificate selection so HTTPS works for each configured domain.

**Architecture:** The proxy has three critical message flow issues: (1) `SetRoute` is sent by `DockerDiscoveryActor` and `ConfigurationService` but never handled by `DomainEntityActor` — replace with `AddDomain` + `AddUpstream`, (2) commands like `AddDomain` don't implement `IWithDomain` so they can't be routed through the shard region's message extractor, (3) `DomainEntityActor` replies `SelectUpstreamForDomain` but `ProxyDispatcher` expects `UpstreamResolved`. After fixing these, add a file-based certificate store and Kestrel SNI selector so each domain gets its own TLS certificate (self-signed for Local mode).

**Tech Stack:** Akka.NET (Cluster Sharding, Persistence), Kestrel SNI (`ServerCertificateSelector`), `System.Security.Cryptography.X509Certificates`, SQLite (Akka.Persistence), xUnit + Akka.Persistence.TestKit

---

## File Structure

### Modified Files

| File | Responsibility |
|------|---------------|
| `src/Schleusenwerk.Core/Persistence/ConfigurationCommands.cs` | Add `IWithDomain` to all domain commands so shard routing works |
| `src/Schleusenwerk/Routing/DomainRouterMessages.cs` | Remove `SetRoute`, fix `ResolveUpstream` to not lowercase the host in EntityId |
| `src/Schleusenwerk/Routing/DomainEntityActor.cs` | Reply `UpstreamResolved` instead of `SelectUpstreamForDomain`, handle `AddUpstream`/`RemoveUpstream` with `IWithDomain`-based routing |
| `src/Schleusenwerk/Forwarding/ProxyDispatcher.cs` | No changes needed after actor-side fix |
| `src/Schleusenwerk/Discovery/DockerDiscoveryActor.cs` | Replace `SetRoute` with `AddDomain` + `AddUpstream` |
| `src/Schleusenwerk/Persistence/ConfigurationService.cs` | Replace `SetRoute` with `AddDomain` + `AddUpstream` |
| `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs` | Replace static fallback cert with `ICertificateStore`-driven SNI selector |
| `src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs` | Register `CertificateProvisioningActor` |
| `src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs` | Update tests for `UpstreamResolved` response type |
| `src/Schleusenwerk.Tests/Persistence/ConfigurationServiceSpec.cs` | Re-enable and rewrite for direct DomainEntityActor Ask pattern |

### New Files

| File | Responsibility |
|------|---------------|
| `src/Schleusenwerk/Certificates/ICertificateStore.cs` | Interface: load/save/list X509Certificate2 by domain |
| `src/Schleusenwerk/Certificates/FileCertificateStore.cs` | File-based impl storing PFX on `/certs` volume |
| `src/Schleusenwerk/Certificates/SelfSignedCertificateGenerator.cs` | Generate self-signed cert for a given domain with SAN |
| `src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs` | Subscribes to `CertificateProvisioningRequested`, generates/stores certs |
| `src/Schleusenwerk/Certificates/SniCertificateSelector.cs` | Kestrel `ServerCertificateSelector` callback using `ICertificateStore` |
| `src/Schleusenwerk.Tests/Certificates/SelfSignedCertificateGeneratorSpec.cs` | Unit tests for cert generation |
| `src/Schleusenwerk.Tests/Certificates/FileCertificateStoreSpec.cs` | Unit tests for file-based cert persistence |
| `src/Schleusenwerk.Tests/Certificates/SniCertificateSelectorSpec.cs` | Unit tests for SNI selection logic |
| `src/Schleusenwerk.Tests/Certificates/CertificateProvisioningActorSpec.cs` | Actor test for provisioning flow |

---

## Task 1: Make Domain Commands Routable via Shard Region

The shard region uses `HashCodeMessageExtractor` with `IWithEntityId.EntityId`. Currently `AddDomain`, `UpdateDomain`, `AddUpstream`, `RemoveUpstream`, `RemoveDomain` (from `Persistence` namespace) don't implement `IWithDomain`, so they can't be routed through sharding. `SetRoute` is used but never handled — we delete it entirely.

**Files:**
- Modify: `src/Schleusenwerk.Core/Persistence/ConfigurationCommands.cs`
- Modify: `src/Schleusenwerk/Routing/DomainRouterMessages.cs`

- [ ] **Step 1: Add IWithDomain to all domain commands in ConfigurationCommands.cs**

Replace the full file content of `src/Schleusenwerk.Core/Persistence/ConfigurationCommands.cs`:

```csharp
using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

public sealed record AddDomain(DomainConfig Config) : IWithDomain
{
    public string Domain => Config.DomainName.Value;
}

public sealed record UpdateDomain(DomainConfig Config) : IWithDomain
{
    public string Domain => Config.DomainName.Value;
}

public sealed record RemoveDomain(DomainName DomainName) : IWithDomain
{
    public string Domain => DomainName.Value;
}

public sealed record AddUpstream(DomainName DomainName, UpstreamTarget Upstream) : IWithDomain
{
    public string Domain => DomainName.Value;
}

public sealed record RemoveUpstream(DomainName DomainName, UpstreamUrl UpstreamUrl) : IWithDomain
{
    public string Domain => DomainName.Value;
}

public sealed record UpdateSettings(ProxySettings Settings);

public sealed record ConfigurationCommandAck
{
    public static ConfigurationCommandAck Instance { get; } = new();
}

public sealed record ConfigurationCommandNack(string Reason);

public sealed record GetConfiguration
{
    public static GetConfiguration Instance { get; } = new();
}

public sealed record DomainConfigResult(DomainConfig Config, IReadOnlyList<UpstreamTarget> Upstreams);

public sealed record GetSettings
{
    public static GetSettings Instance { get; } = new();
}

public sealed record GetUpstreamByUrl(UpstreamUrl Url);

public sealed record UpstreamTargetResult(UpstreamTarget Target);

public sealed record GetAllDomains
{
    public static GetAllDomains Instance { get; } = new();
}

public sealed record AllDomainsResult(IReadOnlyList<DomainName> Domains);

public sealed record GetDomainConfig : IWithDomain
{
    public required string Domain { get; init; }
}
```

Note: `GetDomainConfig` changes from a singleton to requiring a `Domain` so it can be routed via sharding. This replaces the separate `GetDomainByName` message.

- [ ] **Step 2: Remove SetRoute and GetDomainByName from DomainRouterMessages.cs**

In `src/Schleusenwerk/Routing/DomainRouterMessages.cs`, remove the `SetRoute` and `GetDomainByName` records entirely and keep the rest. The full file becomes:

```csharp
namespace Schleusenwerk.Routing;

public interface IWithEntityId
{
    string EntityId { get; }
}

public interface IWithDomain : IWithEntityId
{
    string Domain { get; }
    string IWithEntityId.EntityId => Domain;
}

public interface IWithUrl : IWithEntityId
{
    string Url { get; }
    string IWithEntityId.EntityId => Url;
}

public sealed record RegisterUpstream(UpstreamTarget Target) : IWithUrl
{
    public string Url => Target.Url.Value.ToString();
}

public sealed record SelectUpstreamForDomain(DomainConfig Config, string Url) : IWithUrl;

public sealed record ResolveUpstream(string Host) : IWithDomain
{
    public string Domain => Host.ToLowerInvariant();
}

public sealed record UpstreamResolved(UpstreamTarget Target, DomainConfig Config);

public sealed record UpstreamNotFound(string Host);

public sealed record UpstreamHealthChanged(UpstreamUrl Url, bool IsHealthy) : IWithDomain
{
    public string Domain { get; init; } = "";
}

public sealed record RoutesUpdated(IReadOnlyList<DomainName> Domains);

public sealed record RouteRemoved(DomainName DomainName);
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build errors in files that still reference `SetRoute`, `GetDomainByName`, or the old `GetDomainConfig` singleton. This is expected — we fix those in the next tasks.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk.Core/Persistence/ConfigurationCommands.cs src/Schleusenwerk/Routing/DomainRouterMessages.cs
git commit -m "refactor: make domain commands implement IWithDomain for shard routing, remove SetRoute"
```

---

## Task 2: Fix DomainEntityActor Command Handling

Remove the `PersistenceRemoveDomain` alias (no longer needed since `Persistence.RemoveDomain` now implements `IWithDomain`). Fix `HandleResolveUpstream` to reply `UpstreamResolved` instead of `SelectUpstreamForDomain`. Update `GetDomainConfig` / `GetDomainByName` handling for the new shape.

**Files:**
- Modify: `src/Schleusenwerk/Routing/DomainEntityActor.cs`

- [ ] **Step 1: Rewrite DomainEntityActor.cs**

Replace the full file content of `src/Schleusenwerk/Routing/DomainEntityActor.cs`:

```csharp
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Routing;

public sealed class DomainEntityActor : ReceivePersistentActor, IWithUnboundedStash
{
    public override string PersistenceId
    {
        get
        {
            var name = Self.Path.Name;
            return $"domain-{name}";
        }
    }

    public new IStash Stash { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _upstreamRegion;
    private readonly IActorRef _eventHub;
    private readonly IConfigurationStore _configStore;
    private IMaterializer _materializer = null!;
    private ISourceQueueWithComplete<IClusterEvent>? _publishQueue;

    private DomainConfig? _config;
    private readonly List<UpstreamTarget> _upstreamTargets = [];
    private readonly HashSet<UpstreamUrl> _unhealthyUrls = [];
    private int _roundRobinIndex;

    public DomainEntityActor(IConfigurationStore configStore)
    {
        _configStore = configStore;
        _upstreamRegion = Context.GetActor<UpstreamEntityActor>();
        _eventHub = Context.GetActor<EventHub>();

        Recover<DomainConfigured>(evt => _config = evt.Config);
        Recover<DomainUpstreamAdded>(evt => _upstreamTargets.Add(evt.Target));
        Recover<DomainUpstreamRemoved>(evt => _upstreamTargets.RemoveAll(t => t.Url.Equals(evt.Url)));
        Recover<DomainDeactivated>(_ =>
        {
            _config = null;
            _upstreamTargets.Clear();
        });
        Recover<SnapshotOffer>(_ => { });

        WaitingForPublisher();
    }

    protected override void PreStart()
    {
        base.PreStart();
        _materializer = Context.System.Materializer();
        _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
            .PipeTo(Self);
    }

    private void WaitingForPublisher()
    {
        Command<EventHub.PublisherReady>(msg =>
        {
            var sink = msg.SinkRef.Sink;
            _publishQueue = Source.Queue<IClusterEvent>(100, OverflowStrategy.DropHead)
                .To(sink)
                .Run(_materializer);

            _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe<IDomainEvent>.Instance)
                .PipeTo(Self);

            foreach (var upstream in _upstreamTargets)
            {
                _upstreamRegion.Tell(new RegisterUpstream(upstream));
            }

            Stash.UnstashAll();
            Become(Ready);
        });
        Command<Status.Failure>(f =>
        {
            _log.Warning(f.Cause, "Failed to get publisher from EventHub — retrying");
            _eventHub.Ask<EventHub.PublisherReady>(EventHub.GetPublisher.Instance)
                .PipeTo(Self);
        });
        CommandAny(_ => Stash.Stash());
    }

    private void Ready()
    {
        Command<EventHub.Subscribed>(msg =>
        {
            msg.SourceRef.Source.RunWith(
                Sink.ActorRef<IClusterEvent>(Self, StreamCompleted.Instance, ex => new StreamFailed(ex)),
                _materializer);
        });
        Command<AddDomain>(HandleAddDomain);
        Command<UpdateDomain>(HandleUpdateDomain);
        Command<RemoveDomain>(HandleRemoveDomain);
        Command<AddUpstream>(HandleAddUpstream);
        Command<RemoveUpstream>(HandleRemoveUpstream);
        Command<GetDomainConfig>(_ => HandleGetConfig());
        Command<ResolveUpstream>(HandleResolveUpstream);
        Command<UpstreamHealthChanged>(msg =>
        {
            if (msg.IsHealthy)
            {
                _unhealthyUrls.Remove(msg.Url);
            }
            else
            {
                _unhealthyUrls.Add(msg.Url);
            }
        });
    }

    private void HandleAddDomain(AddDomain cmd)
    {
        if (_config is not null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.Config.DomainName}' already configured."));
            return;
        }

        var evt = new DomainConfigured(cmd.Config);
        Persist(evt, persisted =>
        {
            _config = persisted.Config;
            _configStore.UpsertDomainAsync(persisted.Config);
            PublishEvent(persisted);
            PublishEvent(new CertificateProvisioningRequested(cmd.Config.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleUpdateDomain(UpdateDomain cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.Config.DomainName}' does not exist."));
            return;
        }

        var evt = new DomainConfigured(cmd.Config);
        Persist(evt, persisted =>
        {
            _config = persisted.Config;
            _configStore.UpsertDomainAsync(persisted.Config);
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleRemoveDomain(RemoveDomain cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack($"Domain '{cmd.DomainName}' does not exist."));
            return;
        }

        var evt = new DomainDeactivated(cmd.DomainName);
        Persist(evt, persisted =>
        {
            _config = null;
            _upstreamTargets.Clear();
            _configStore.RemoveDomainAsync(cmd.DomainName);
            PublishEvent(persisted);
            Context.System.EventStream.Publish(new RouteRemoved(cmd.DomainName));
            Sender.Tell(ConfigurationCommandAck.Instance);
            Self.Tell(PoisonPill.Instance);
        });
    }

    private void HandleAddUpstream(AddUpstream cmd)
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack("Domain not configured yet."));
            return;
        }

        if (_upstreamTargets.Any(t => t.Url.Equals(cmd.Upstream.Url)))
        {
            Sender.Tell(new ConfigurationCommandNack($"Upstream '{cmd.Upstream.Url}' already exists."));
            return;
        }

        var evt = new DomainUpstreamAdded(cmd.Upstream);
        Persist(evt, persisted =>
        {
            _upstreamTargets.Add(persisted.Target);
            _upstreamRegion.Tell(new RegisterUpstream(persisted.Target));
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleRemoveUpstream(RemoveUpstream cmd)
    {
        if (!_upstreamTargets.Any(t => t.Url.Equals(cmd.UpstreamUrl)))
        {
            Sender.Tell(new ConfigurationCommandNack($"Upstream '{cmd.UpstreamUrl}' does not exist."));
            return;
        }

        var evt = new DomainUpstreamRemoved(cmd.UpstreamUrl);
        Persist(evt, persisted =>
        {
            _upstreamTargets.RemoveAll(t => t.Url.Equals(persisted.Url));
            _unhealthyUrls.Remove(persisted.Url);
            PublishEvent(persisted);
            Sender.Tell(ConfigurationCommandAck.Instance);
        });
    }

    private void HandleGetConfig()
    {
        if (_config is null)
        {
            Sender.Tell(new ConfigurationCommandNack("Domain not configured."));
            return;
        }

        Sender.Tell(new DomainConfigResult(_config, _upstreamTargets));
    }

    private void HandleResolveUpstream(ResolveUpstream msg)
    {
        if (_config is null)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var healthy = _upstreamTargets.Where(u => !_unhealthyUrls.Contains(u.Url)).ToList();
        if (healthy.Count == 0)
        {
            Sender.Tell(new UpstreamNotFound(msg.Host));
            return;
        }

        var picked = healthy[_roundRobinIndex % healthy.Count];
        _roundRobinIndex++;
        Sender.Tell(new UpstreamResolved(picked, _config));
    }

    private void PublishEvent(IClusterEvent evt)
    {
        _publishQueue?.OfferAsync(evt).PipeTo(Self,
            success: r => r is QueueOfferResult.Dropped
                ? new PublishDropped(evt)
                : Akka.Done.Instance,
            failure: ex => new PublishFailed(ex));
    }

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Ex);

    private sealed record PublishDropped(IClusterEvent Event);
    private sealed record PublishFailed(Exception Exception);
}
```

Key changes:
- Removed `PersistenceRemoveDomain` alias — `RemoveDomain` from `Persistence` namespace now has `IWithDomain` directly
- Removed `GetDomainByName` handler — `GetDomainConfig` now carries the domain for routing
- `HandleResolveUpstream` returns `UpstreamResolved(picked, _config)` instead of `SelectUpstreamForDomain`

- [ ] **Step 2: Verify build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Errors in ConfigurationService.cs and DockerDiscoveryActor.cs (still reference `SetRoute`/`GetDomainByName`). DomainEntityActorSpec may also have errors.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Routing/DomainEntityActor.cs
git commit -m "fix: DomainEntityActor replies UpstreamResolved, removes SetRoute/GetDomainByName handling"
```

---

## Task 3: Fix DockerDiscoveryActor to Use AddDomain + AddUpstream

Replace `SetRoute` with two separate Ask calls: `AddDomain` (idempotent — may NACK if already exists, that's fine) then `AddUpstream`. Same for `RemoveDomain`.

**Files:**
- Modify: `src/Schleusenwerk/Discovery/DockerDiscoveryActor.cs`

- [ ] **Step 1: Update RegisterContainerIfLabeled and UnregisterContainer**

In `DockerDiscoveryActor.cs`, replace the `RegisterContainerIfLabeled` method:

```csharp
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

    _domainRegion.Ask<object>(new AddDomain(domainConfig), AskTimeout)
        .ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                return (object)new AddUpstream(parsed.Domain, parsed.Upstream);
            }
            return new RegisterFailed(containerId, parsed.Domain, t.Exception);
        })
        .PipeTo(Self);

    _log.Info("Registered container {Id} → {Domain} @ {Url}", containerId[..12], parsed.Domain, parsed.Upstream.Url);
}
```

Add a handler in the constructor for `AddUpstream` (forwarded to domain region) and `RegisterFailed`:

```csharp
Receive<AddUpstream>(msg => _domainRegion.Tell(msg));
Receive<RegisterFailed>(msg =>
    _log.Warning("Failed to register container {Id} for domain {Domain}: {Error}",
        msg.ContainerId[..12], msg.Domain, msg.Error?.Message ?? "unknown"));
```

Add the `RegisterFailed` message class inside the actor:

```csharp
private sealed record RegisterFailed(string ContainerId, DomainName Domain, Exception? Error);
```

Update `UnregisterContainer` — replace `RemoveDomain(entry.Domain)` with the persistence `RemoveDomain`:

```csharp
private void UnregisterContainer(string containerId)
{
    if (!_tracked.TryGetValue(containerId, out var entry))
    {
        return;
    }

    _tracked.Remove(containerId);
    _domainRegion.Tell(new Persistence.RemoveUpstream(entry.Domain, entry.Url));
    _log.Info("Unregistered container {Id} upstream {Url}", containerId[..12], entry.Url);
}
```

Note: We remove the specific upstream, not the entire domain. Another container may still be serving the same domain.

Also add the required using at the top:

```csharp
using Schleusenwerk.Persistence;
```

- [ ] **Step 2: Verify build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Errors remain in ConfigurationService.cs (still references `SetRoute`/`GetDomainByName`).

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Discovery/DockerDiscoveryActor.cs
git commit -m "fix: DockerDiscoveryActor uses AddDomain + AddUpstream instead of SetRoute"
```

---

## Task 4: Fix ConfigurationService to Use Proper Commands

Replace all `SetRoute` and `GetDomainByName` usage with `AddDomain`, `UpdateDomain`, `AddUpstream`, `RemoveUpstream`, `GetDomainConfig`.

**Files:**
- Modify: `src/Schleusenwerk/Persistence/ConfigurationService.cs`

- [ ] **Step 1: Rewrite ConfigurationService.cs**

Replace the full file content of `src/Schleusenwerk/Persistence/ConfigurationService.cs`:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

public sealed class ConfigurationService : IConfigurationService
{
    private readonly IActorRef _domainRegion;
    private readonly IConfigurationStore _store;
    private readonly TimeSpan _timeout;

    public ConfigurationService(IReadOnlyActorRegistry registry, IConfigurationStore store, TimeSpan? timeout = null)
    {
        _domainRegion = registry.Get<DomainEntityActor>();
        _store = store;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<ConfigurationResult<ConfigurationSnapshot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var domains = await _store.GetAllDomainsAsync(cancellationToken);
        var settings = await _store.GetSettingsAsync(cancellationToken);

        var snapshot = new ConfigurationSnapshot
        {
            Domains = domains.ToList(),
            Upstreams = new Dictionary<string, IReadOnlyList<UpstreamTarget>>(),
            Certificates = new Dictionary<string, CertificateInfo>(),
            Settings = settings,
        };

        return new ConfigurationResult<ConfigurationSnapshot>.Success(snapshot);
    }

    public async Task<ConfigurationResult<DomainConfigResult>> GetByDomainAsync(
        DomainName domainName, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new GetDomainConfig { Domain = domainName.Value }, _timeout, cancellationToken);

        return result switch
        {
            DomainConfigResult domainResult => new ConfigurationResult<DomainConfigResult>.Success(domainResult),
            ConfigurationCommandNack nack => new ConfigurationResult<DomainConfigResult>.Failure(nack.Reason),
            _ => new ConfigurationResult<DomainConfigResult>.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> AddDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new AddDomain(config), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> UpdateDomainAsync(DomainConfig config, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new UpdateDomain(config), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> RemoveDomainAsync(DomainName domainName, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new RemoveDomain(domainName), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> AddUpstreamAsync(
        DomainName domainName, UpstreamTarget upstream, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new AddUpstream(domainName, upstream), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult> RemoveUpstreamAsync(
        DomainName domainName, UpstreamUrl upstreamUrl, CancellationToken cancellationToken = default)
    {
        var result = await _domainRegion.Ask<object>(
            new RemoveUpstream(domainName, upstreamUrl), _timeout, cancellationToken);

        return result switch
        {
            ConfigurationCommandAck => ConfigurationResult.Success.Instance,
            ConfigurationCommandNack nack => new ConfigurationResult.Failure(nack.Reason),
            _ => new ConfigurationResult.Failure($"Unexpected response type: {result.GetType().Name}"),
        };
    }

    public async Task<ConfigurationResult<ProxySettings>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _store.GetSettingsAsync(cancellationToken);
        return new ConfigurationResult<ProxySettings>.Success(settings);
    }

    public async Task<ConfigurationResult> UpdateSettingsAsync(
        ProxySettings settings, CancellationToken cancellationToken = default)
    {
        await _store.UpdateSettingsAsync(settings, cancellationToken);
        return ConfigurationResult.Success.Instance;
    }

    public async Task<ConfigurationResult<string>> ExportAsync(
        ConfigurationExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshotResult = await GetAllAsync(cancellationToken);

        if (snapshotResult is ConfigurationResult<ConfigurationSnapshot>.Failure failure)
        {
            return new ConfigurationResult<string>.Failure(failure.Error);
        }

        var snapshot = ((ConfigurationResult<ConfigurationSnapshot>.Success)snapshotResult).Value;
        var json = ConfigurationExporter.ToJson(snapshot, options);
        return new ConfigurationResult<string>.Success(json);
    }
}
```

Key changes:
- `AddDomainAsync` sends `AddDomain` directly (no `SetRoute`)
- `UpdateDomainAsync` sends `UpdateDomain` directly (no query-then-SetRoute)
- `AddUpstreamAsync` sends `AddUpstream` directly (actor validates domain exists)
- `RemoveUpstreamAsync` sends `RemoveUpstream` directly (actor validates)
- `GetByDomainAsync` uses `new GetDomainConfig { Domain = ... }` instead of `GetDomainByName`

- [ ] **Step 2: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: 0 errors. The `SetRoute` and `GetDomainByName` types are now fully unused and were already removed in Task 1.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Persistence/ConfigurationService.cs
git commit -m "fix: ConfigurationService uses AddDomain/AddUpstream commands directly"
```

---

## Task 5: Update DomainEntityActorSpec Tests

Tests currently expect `SelectUpstreamForDomain` from `ResolveUpstream` — update to expect `UpstreamResolved`. Also update `AddUpstream`/`RemoveUpstream` calls to use the `DomainName` parameter.

**Files:**
- Modify: `src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs`

- [ ] **Step 1: Update test expectations**

In the round-robin test, change `Ask<SelectUpstreamForDomain>` to `Ask<UpstreamResolved>`:

```csharp
var hosts = new List<string>();
for (var i = 0; i < 6; i++)
{
    var fwd = await entity.Ask<UpstreamResolved>(
        new ResolveUpstream("example.com"), Timeout);
    hosts.Add(fwd.Target.Url.Value.Host);
}
```

In the health state test, change `Ask<SelectUpstreamForDomain>` to `Ask<UpstreamResolved>`:

```csharp
for (var i = 0; i < 4; i++)
{
    var fwd = await entity.Ask<UpstreamResolved>(
        new ResolveUpstream("example.com"), Timeout);
    Assert.Contains("b", fwd.Target.Url.Value.Host);
}
```

And the second loop:

```csharp
var hosts = new List<string>();
for (var i = 0; i < 4; i++)
{
    var fwd = await entity.Ask<UpstreamResolved>(
        new ResolveUpstream("example.com"), Timeout);
    hosts.Add(fwd.Target.Url.Value.Host);
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Routing.DomainEntityActorSpec"`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.Tests/Routing/DomainEntityActorSpec.cs
git commit -m "test: update DomainEntityActorSpec for UpstreamResolved response type"
```

---

## Task 6: Re-enable and Fix ConfigurationServiceSpec

The tests are currently `#if false`. Rewrite them to work with the new direct-command pattern against real DomainEntityActor instances (no ConfigurationPersistenceActor needed).

**Files:**
- Modify: `src/Schleusenwerk.Tests/Persistence/ConfigurationServiceSpec.cs`

- [ ] **Step 1: Rewrite ConfigurationServiceSpec.cs**

Replace the full file content:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Persistence;

public sealed class ConfigurationServiceSpec : PersistenceTestKit
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private int _serviceCounter;

    private ConfigurationService CreateService()
    {
        var id = Interlocked.Increment(ref _serviceCounter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-svc-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var upstreamProbe = CreateTestProbe();
        registry.Register<UpstreamEntityActor>(upstreamProbe, overwrite: true);

        var store = new SqliteConfigurationStore(
            $"Data Source=test-svc-{id}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        var domainActor = Sys.ActorOf(
            Props.Create(() => new DomainEntityActor(store)),
            $"domain-svc-{id}");
        registry.Register<DomainEntityActor>(domainActor, overwrite: true);

        return new ConfigurationService(registry, store, Timeout);
    }

    private static DomainConfig CreateDomainConfig(string domain)
    {
        return new DomainConfig { DomainName = DomainName.Parse(domain) };
    }

    private static UpstreamTarget CreateUpstream(string url)
    {
        return UpstreamTarget.Create(url);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomainAsync_should_return_success()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");

        var result = await service.AddDomainAsync(config);

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public async Task AddDomainAsync_should_return_failure_on_duplicate()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");

        await service.AddDomainAsync(config);
        var result = await service.AddDomainAsync(config);

        Assert.IsType<ConfigurationResult.Failure>(result);
        Assert.Contains("already configured", ((ConfigurationResult.Failure)result).Error);
    }

    [Fact(Timeout = 5000)]
    public async Task GetByDomainAsync_should_return_domain_config()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.GetByDomainAsync(DomainName.Parse("example.com"));

        Assert.IsType<ConfigurationResult<DomainConfigResult>.Success>(result);
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)result).Value;
        Assert.Equal("example.com", domainResult.Config.DomainName.Value);
    }

    [Fact(Timeout = 5000)]
    public async Task AddUpstreamAsync_should_add_upstream_to_domain()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.AddUpstreamAsync(
            DomainName.Parse("example.com"),
            CreateUpstream("http://localhost:8080"));

        Assert.True(result.IsSuccess);

        var queryResult = await service.GetByDomainAsync(DomainName.Parse("example.com"));
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)queryResult).Value;
        Assert.Single(domainResult.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveUpstreamAsync_should_remove_upstream()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);
        await service.AddUpstreamAsync(
            DomainName.Parse("example.com"),
            CreateUpstream("http://localhost:8080"));

        var result = await service.RemoveUpstreamAsync(
            DomainName.Parse("example.com"),
            UpstreamUrl.Parse("http://localhost:8080"));

        Assert.True(result.IsSuccess);

        var queryResult = await service.GetByDomainAsync(DomainName.Parse("example.com"));
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)queryResult).Value;
        Assert.Empty(domainResult.Upstreams);
    }

    [Fact(Timeout = 5000)]
    public async Task RemoveDomainAsync_should_return_success()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.RemoveDomainAsync(DomainName.Parse("example.com"));

        Assert.True(result.IsSuccess);
    }

    [Fact(Timeout = 5000)]
    public async Task UpdateDomainAsync_should_update_existing()
    {
        var service = CreateService();
        var config = CreateDomainConfig("example.com");
        await service.AddDomainAsync(config);

        var result = await service.UpdateDomainAsync(config with { ForceHttps = true });

        Assert.True(result.IsSuccess);
        var queryResult = await service.GetByDomainAsync(DomainName.Parse("example.com"));
        var domainResult = ((ConfigurationResult<DomainConfigResult>.Success)queryResult).Value;
        Assert.True(domainResult.Config.ForceHttps);
    }

    [Fact(Timeout = 5000)]
    public async Task GetSettingsAsync_should_return_default_settings()
    {
        var service = CreateService();

        var result = await service.GetSettingsAsync();

        Assert.IsType<ConfigurationResult<ProxySettings>.Success>(result);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Persistence.ConfigurationServiceSpec"`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk.Tests/Persistence/ConfigurationServiceSpec.cs
git commit -m "test: re-enable ConfigurationServiceSpec with direct command pattern"
```

---

## Task 7: Run Full Test Suite

Verify all existing tests still pass after the message refactoring.

**Files:** None (verification only)

- [ ] **Step 1: Build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 3: Fix any failures**

If any test fails, fix the test or implementation before proceeding. Common issues:
- `ProxyDispatcherSpec` might reference `SetRoute` or `GetDomainByName`
- `DomainEntityHealthSpec` might expect `SelectUpstreamForDomain`

---

## Task 8: Certificate Store Interface and File Implementation

Create the file-based certificate store that persists PFX files to `/certs` volume.

**Files:**
- Create: `src/Schleusenwerk/Certificates/ICertificateStore.cs`
- Create: `src/Schleusenwerk/Certificates/FileCertificateStore.cs`
- Create: `src/Schleusenwerk.Tests/Certificates/FileCertificateStoreSpec.cs`

- [ ] **Step 1: Write ICertificateStore interface**

Create `src/Schleusenwerk/Certificates/ICertificateStore.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public interface ICertificateStore
{
    X509Certificate2? GetCertificate(DomainName domain);
    void StoreCertificate(DomainName domain, X509Certificate2 certificate);
    bool HasCertificate(DomainName domain);
    IReadOnlyList<DomainName> ListDomains();
}
```

- [ ] **Step 2: Write FileCertificateStoreSpec tests**

Create `src/Schleusenwerk.Tests/Certificates/FileCertificateStoreSpec.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class FileCertificateStoreSpec : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"cert-test-{Guid.NewGuid():N}");

    public FileCertificateStoreSpec()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private FileCertificateStore CreateStore() => new(_tempDir);

    private static X509Certificate2 CreateTestCert(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    [Fact(Timeout = 5000)]
    public void GetCertificate_should_return_null_when_not_stored()
    {
        var store = CreateStore();

        var result = store.GetCertificate(DomainName.Parse("example.com"));

        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public void StoreCertificate_should_persist_and_retrieve()
    {
        var store = CreateStore();
        var domain = DomainName.Parse("example.com");
        using var cert = CreateTestCert("example.com");

        store.StoreCertificate(domain, cert);
        using var loaded = store.GetCertificate(domain);

        Assert.NotNull(loaded);
        Assert.Equal(cert.Thumbprint, loaded.Thumbprint);
    }

    [Fact(Timeout = 5000)]
    public void HasCertificate_should_return_true_after_store()
    {
        var store = CreateStore();
        var domain = DomainName.Parse("example.com");
        using var cert = CreateTestCert("example.com");

        store.StoreCertificate(domain, cert);

        Assert.True(store.HasCertificate(domain));
    }

    [Fact(Timeout = 5000)]
    public void HasCertificate_should_return_false_when_not_stored()
    {
        var store = CreateStore();

        Assert.False(store.HasCertificate(DomainName.Parse("missing.com")));
    }

    [Fact(Timeout = 5000)]
    public void ListDomains_should_return_all_stored_domains()
    {
        var store = CreateStore();
        using var cert1 = CreateTestCert("a.com");
        using var cert2 = CreateTestCert("b.com");

        store.StoreCertificate(DomainName.Parse("a.com"), cert1);
        store.StoreCertificate(DomainName.Parse("b.com"), cert2);

        var domains = store.ListDomains();
        Assert.Equal(2, domains.Count);
        Assert.Contains(DomainName.Parse("a.com"), domains);
        Assert.Contains(DomainName.Parse("b.com"), domains);
    }

    [Fact(Timeout = 5000)]
    public void GetCertificate_should_survive_new_store_instance()
    {
        var domain = DomainName.Parse("example.com");
        using var cert = CreateTestCert("example.com");

        var store1 = CreateStore();
        store1.StoreCertificate(domain, cert);

        var store2 = CreateStore();
        using var loaded = store2.GetCertificate(domain);

        Assert.NotNull(loaded);
        Assert.Equal(cert.Thumbprint, loaded.Thumbprint);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.FileCertificateStoreSpec"`
Expected: FAIL — `FileCertificateStore` does not exist yet.

- [ ] **Step 4: Implement FileCertificateStore**

Create `src/Schleusenwerk/Certificates/FileCertificateStore.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public sealed class FileCertificateStore : ICertificateStore
{
    private readonly string _basePath;

    public FileCertificateStore(string basePath)
    {
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public X509Certificate2? GetCertificate(DomainName domain)
    {
        var path = GetCertPath(domain);
        if (!File.Exists(path))
        {
            return null;
        }

        return new X509Certificate2(path);
    }

    public void StoreCertificate(DomainName domain, X509Certificate2 certificate)
    {
        var path = GetCertPath(domain);
        var pfxBytes = certificate.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfxBytes);
    }

    public bool HasCertificate(DomainName domain)
    {
        return File.Exists(GetCertPath(domain));
    }

    public IReadOnlyList<DomainName> ListDomains()
    {
        if (!Directory.Exists(_basePath))
        {
            return [];
        }

        return Directory.GetFiles(_basePath, "*.pfx")
            .Select(f => DomainName.Parse(Path.GetFileNameWithoutExtension(f)))
            .ToList();
    }

    private string GetCertPath(DomainName domain) =>
        Path.Combine(_basePath, $"{domain.Value}.pfx");
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.FileCertificateStoreSpec"`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Schleusenwerk/Certificates/ICertificateStore.cs src/Schleusenwerk/Certificates/FileCertificateStore.cs src/Schleusenwerk.Tests/Certificates/FileCertificateStoreSpec.cs
git commit -m "feat: add ICertificateStore and FileCertificateStore for per-domain PFX persistence"
```

---

## Task 9: Self-Signed Certificate Generator

Generate a self-signed cert with a Subject Alternative Name (SAN) for a given domain. Used when `AcmeStage.Local` or as fallback.

**Files:**
- Create: `src/Schleusenwerk/Certificates/SelfSignedCertificateGenerator.cs`
- Create: `src/Schleusenwerk.Tests/Certificates/SelfSignedCertificateGeneratorSpec.cs`

- [ ] **Step 1: Write SelfSignedCertificateGeneratorSpec tests**

Create `src/Schleusenwerk.Tests/Certificates/SelfSignedCertificateGeneratorSpec.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class SelfSignedCertificateGeneratorSpec
{
    [Fact(Timeout = 5000)]
    public void Generate_should_return_certificate_with_correct_subject()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        Assert.Contains("CN=example.com", cert.Subject);
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_return_certificate_with_private_key()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        Assert.True(cert.HasPrivateKey);
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_include_san_extension()
    {
        var domain = DomainName.Parse("app.example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        var sanExtension = cert.Extensions
            .OfType<X509Extension>()
            .FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");

        Assert.NotNull(sanExtension);
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_have_valid_date_range()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        Assert.True(cert.NotBefore <= DateTime.UtcNow);
        Assert.True(cert.NotAfter > DateTime.UtcNow.AddDays(300));
    }

    [Fact(Timeout = 5000)]
    public void Generate_should_have_server_auth_eku()
    {
        var domain = DomainName.Parse("example.com");

        using var cert = SelfSignedCertificateGenerator.Generate(domain);

        var eku = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        Assert.NotNull(eku);
        Assert.Contains(eku.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>(),
            o => o.Value == "1.3.6.1.5.5.7.3.1");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.SelfSignedCertificateGeneratorSpec"`
Expected: FAIL — class does not exist.

- [ ] **Step 3: Implement SelfSignedCertificateGenerator**

Create `src/Schleusenwerk/Certificates/SelfSignedCertificateGenerator.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public static class SelfSignedCertificateGenerator
{
    public static X509Certificate2 Generate(DomainName domain, TimeSpan? validity = null)
    {
        var effectiveValidity = validity ?? TimeSpan.FromDays(365);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={domain.Value}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(domain.Value);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.Add(effectiveValidity));

        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.SelfSignedCertificateGeneratorSpec"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/Certificates/SelfSignedCertificateGenerator.cs src/Schleusenwerk.Tests/Certificates/SelfSignedCertificateGeneratorSpec.cs
git commit -m "feat: add SelfSignedCertificateGenerator with SAN support"
```

---

## Task 10: SNI Certificate Selector

Kestrel callback that looks up the certificate from `ICertificateStore` by SNI hostname. Falls back to a default self-signed cert for unknown domains.

**Files:**
- Create: `src/Schleusenwerk/Certificates/SniCertificateSelector.cs`
- Create: `src/Schleusenwerk.Tests/Certificates/SniCertificateSelectorSpec.cs`

- [ ] **Step 1: Write SniCertificateSelectorSpec tests**

Create `src/Schleusenwerk.Tests/Certificates/SniCertificateSelectorSpec.cs`:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Certificates;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class SniCertificateSelectorSpec : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"sni-test-{Guid.NewGuid():N}");
    private readonly FileCertificateStore _store;
    private readonly SniCertificateSelector _selector;

    public SniCertificateSelectorSpec()
    {
        Directory.CreateDirectory(_tempDir);
        _store = new FileCertificateStore(_tempDir);
        _selector = new SniCertificateSelector(_store);
    }

    public void Dispose()
    {
        _selector.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact(Timeout = 5000)]
    public void Select_should_return_stored_certificate_for_known_domain()
    {
        var domain = DomainName.Parse("example.com");
        using var cert = SelfSignedCertificateGenerator.Generate(domain);
        _store.StoreCertificate(domain, cert);

        using var selected = _selector.Select("example.com");

        Assert.NotNull(selected);
        Assert.Equal(cert.Thumbprint, selected.Thumbprint);
    }

    [Fact(Timeout = 5000)]
    public void Select_should_return_fallback_for_unknown_domain()
    {
        using var selected = _selector.Select("unknown.com");

        Assert.NotNull(selected);
        Assert.Contains("CN=localhost", selected.Subject);
    }

    [Fact(Timeout = 5000)]
    public void Select_should_return_fallback_for_null_hostname()
    {
        using var selected = _selector.Select(null);

        Assert.NotNull(selected);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.SniCertificateSelectorSpec"`
Expected: FAIL — `SniCertificateSelector` does not exist.

- [ ] **Step 3: Implement SniCertificateSelector**

Create `src/Schleusenwerk/Certificates/SniCertificateSelector.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public sealed class SniCertificateSelector : IDisposable
{
    private readonly ICertificateStore _store;
    private readonly Lazy<X509Certificate2> _fallback;

    public SniCertificateSelector(ICertificateStore store)
    {
        _store = store;
        _fallback = new Lazy<X509Certificate2>(
            () => SelfSignedCertificateGenerator.Generate(DomainName.Parse("localhost")));
    }

    public X509Certificate2 Select(string? hostname)
    {
        if (!string.IsNullOrEmpty(hostname) && DomainName.TryParse(hostname, out var domain))
        {
            var cert = _store.GetCertificate(domain);
            if (cert is not null)
            {
                return cert;
            }
        }

        return _fallback.Value;
    }

    public void Dispose()
    {
        if (_fallback.IsValueCreated)
        {
            _fallback.Value.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.SniCertificateSelectorSpec"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/Certificates/SniCertificateSelector.cs src/Schleusenwerk.Tests/Certificates/SniCertificateSelectorSpec.cs
git commit -m "feat: add SniCertificateSelector with fallback to localhost cert"
```

---

## Task 11: CertificateProvisioningActor

Subscribes to `CertificateProvisioningRequested` events via EventHub. On receiving an event, generates a self-signed cert (for Local/Staging mode) and stores it via `ICertificateStore`.

**Files:**
- Create: `src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs`
- Create: `src/Schleusenwerk.Tests/Certificates/CertificateProvisioningActorSpec.cs`

- [ ] **Step 1: Write CertificateProvisioningActorSpec tests**

Create `src/Schleusenwerk.Tests/Certificates/CertificateProvisioningActorSpec.cs`:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.TestKit;
using Schleusenwerk.Certificates;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Xunit;

namespace Schleusenwerk.Tests.Certificates;

public sealed class CertificateProvisioningActorSpec : PersistenceTestKit, IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"prov-test-{Guid.NewGuid():N}");
    private int _actorCounter;

    public CertificateProvisioningActorSpec()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private (IActorRef actor, FileCertificateStore store) CreateActor()
    {
        var id = Interlocked.Increment(ref _actorCounter);
        var registry = ActorRegistry.For(Sys);

        var hub = Sys.ActorOf(Props.Create<EventHub>(), $"hub-prov-{id}");
        registry.Register<EventHub>(hub, overwrite: true);

        var store = new FileCertificateStore(_tempDir);
        var actor = Sys.ActorOf(
            Props.Create(() => new CertificateProvisioningActor(store)),
            $"cert-prov-{id}");

        return (actor, store);
    }

    [Fact(Timeout = 5000)]
    public async Task CertificateProvisioningActor_should_generate_cert_on_request()
    {
        var (actor, store) = CreateActor();
        var domain = DomainName.Parse("example.com");

        actor.Tell(new CertificateProvisioningRequested(domain));

        await Task.Delay(500);

        Assert.True(store.HasCertificate(domain));
        using var cert = store.GetCertificate(domain);
        Assert.NotNull(cert);
        Assert.Contains("CN=example.com", cert.Subject);
    }

    [Fact(Timeout = 5000)]
    public async Task CertificateProvisioningActor_should_skip_if_cert_already_exists()
    {
        var (actor, store) = CreateActor();
        var domain = DomainName.Parse("example.com");

        using var existingCert = SelfSignedCertificateGenerator.Generate(domain);
        store.StoreCertificate(domain, existingCert);
        var originalThumbprint = existingCert.Thumbprint;

        actor.Tell(new CertificateProvisioningRequested(domain));

        await Task.Delay(500);

        using var loaded = store.GetCertificate(domain);
        Assert.Equal(originalThumbprint, loaded!.Thumbprint);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.CertificateProvisioningActorSpec"`
Expected: FAIL — `CertificateProvisioningActor` does not exist.

- [ ] **Step 3: Implement CertificateProvisioningActor**

Create `src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs`:

```csharp
using Akka.Actor;
using Akka.Event;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;

namespace Schleusenwerk.Certificates;

public sealed class CertificateProvisioningActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ICertificateStore _store;

    public CertificateProvisioningActor(ICertificateStore store)
    {
        _store = store;

        Receive<CertificateProvisioningRequested>(Handle);
    }

    private void Handle(CertificateProvisioningRequested msg)
    {
        if (_store.HasCertificate(msg.DomainName))
        {
            _log.Info("Certificate already exists for {Domain}, skipping", msg.DomainName);
            return;
        }

        _log.Info("Generating self-signed certificate for {Domain}", msg.DomainName);
        using var cert = SelfSignedCertificateGenerator.Generate(msg.DomainName);
        _store.StoreCertificate(msg.DomainName, cert);
        _log.Info("Stored self-signed certificate for {Domain}", msg.DomainName);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj -- -class "Schleusenwerk.Tests.Certificates.CertificateProvisioningActorSpec"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs src/Schleusenwerk.Tests/Certificates/CertificateProvisioningActorSpec.cs
git commit -m "feat: add CertificateProvisioningActor for self-signed cert generation"
```

---

## Task 12: Wire Up Certificates in Startup

Register `ICertificateStore`, `SniCertificateSelector`, and `CertificateProvisioningActor` in DI and actor system. Replace the static fallback cert with SNI selector.

**Files:**
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs`

- [ ] **Step 1: Update SchleusenwerkServicesSetup.cs**

Replace the full file:

```csharp
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Schleusenwerk.Certificates;
using Schleusenwerk.Forwarding;
using Schleusenwerk.HealthCheck;
using Schleusenwerk.Persistence;
using Servus.Core.Application.Startup;
using TurboHTTP;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkServicesSetup : IServiceSetupContainer
{
    public void SetupServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();
        services.AddSingleton<IHealthCheckPropsFactory, HealthCheckPropsFactory>();
        services.AddTurboHttpClient();
        services.AddSingleton<RequestForwardingPipeline>();
        services.AddSingleton<HeaderManipulationFilter>();
        services.AddSingleton<WebSocketTunnel>();
        services.AddSingleton<IProxyDispatcher, ProxyDispatcher>();

        var connectionString = configuration["Akka:Persistence:ConnectionString"]
            ?? "Data Source=/data/schleusenwerk.db";
        services.AddSingleton<IConfigurationStore>(new SqliteConfigurationStore(connectionString));
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        var certsPath = configuration["Certificates:Path"] ?? "/certs";
        services.AddSingleton<ICertificateStore>(new FileCertificateStore(certsPath));
        services.AddSingleton<SniCertificateSelector>();

        services.Configure<KestrelServerOptions>(options =>
        {
            options.ConfigureHttpsDefaults(adapterOptions =>
            {
                var selector = options.ApplicationServices!.GetRequiredService<SniCertificateSelector>();
                adapterOptions.ServerCertificateSelector = (_, hostname) => selector.Select(hostname);
            });
        });
    }
}
```

- [ ] **Step 2: Register CertificateProvisioningActor in actor system setup**

In `SchleusenwerkActorSystemSetup.cs`, add to the `WithActors` block:

```csharp
builder.WithActors((system, registry, resolver) =>
{
    var eventHub = system.ActorOf(resolver.Props<EventHub>(), "eventHub");
    registry.Register<EventHub>(eventHub);

    var dockerDiscovery = system.ActorOf(resolver.Props<DockerDiscoveryActor>(), "docker-discovery");
    registry.Register<DockerDiscoveryActor>(dockerDiscovery);

    var certProvisioning = system.ActorOf(resolver.Props<CertificateProvisioningActor>(), "cert-provisioning");
    registry.Register<CertificateProvisioningActor>(certProvisioning);
});
```

Add the using at the top:

```csharp
using Schleusenwerk.Certificates;
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: 0 errors.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs
git commit -m "feat: wire up SNI certificate selector and CertificateProvisioningActor in startup"
```

---

## Task 13: Connect CertificateProvisioningActor to EventHub

The actor needs to subscribe to `ICertificateEvent` via EventHub so it receives `CertificateProvisioningRequested` events published by `DomainEntityActor`.

**Files:**
- Modify: `src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs`

- [ ] **Step 1: Add EventHub subscription in PreStart**

Update `CertificateProvisioningActor.cs`:

```csharp
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Persistence;
using Servus.Akka;

namespace Schleusenwerk.Certificates;

public sealed class CertificateProvisioningActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ICertificateStore _store;
    private readonly IActorRef _eventHub;

    public CertificateProvisioningActor(ICertificateStore store)
    {
        _store = store;
        _eventHub = Context.GetActor<EventHub>();

        Receive<CertificateProvisioningRequested>(Handle);
        Receive<EventHub.Subscribed>(msg =>
        {
            msg.SourceRef.Source
                .RunWith(
                    Sink.ActorRef<IClusterEvent>(Self, StreamCompleted.Instance, ex => new StreamFailed(ex)),
                    Context.Materializer());
        });
        Receive<StreamCompleted>(_ =>
            _log.Warning("Certificate event stream completed unexpectedly"));
        Receive<StreamFailed>(msg =>
            _log.Error(msg.Ex, "Certificate event stream failed"));
    }

    protected override void PreStart()
    {
        base.PreStart();
        _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe<ICertificateEvent>.Instance)
            .PipeTo(Self);
    }

    private void Handle(CertificateProvisioningRequested msg)
    {
        if (_store.HasCertificate(msg.DomainName))
        {
            _log.Info("Certificate already exists for {Domain}, skipping", msg.DomainName);
            return;
        }

        _log.Info("Generating self-signed certificate for {Domain}", msg.DomainName);
        using var cert = SelfSignedCertificateGenerator.Generate(msg.DomainName);
        _store.StoreCertificate(msg.DomainName, cert);
        _log.Info("Stored self-signed certificate for {Domain}", msg.DomainName);
    }

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Ex);
}
```

- [ ] **Step 2: Run full test suite**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs
git commit -m "feat: CertificateProvisioningActor subscribes to ICertificateEvent via EventHub"
```

---

## Task 14: Final Verification

Run the full build and all tests to ensure everything is green.

**Files:** None (verification only)

- [ ] **Step 1: Clean build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 3: Verify no references to SetRoute or GetDomainByName remain**

Run: `grep -r "SetRoute\|GetDomainByName" src/ --include="*.cs" | grep -v "obj/" | grep -v "#if false"`
Expected: No matches.
