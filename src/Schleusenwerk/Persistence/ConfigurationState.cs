using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Event-sourced state for the configuration persistence actor.
/// Rebuilt from journal events on recovery.
/// </summary>
public sealed class ConfigurationState
{
    private readonly Dictionary<DomainName, DomainConfig> _domains = new();
    private readonly Dictionary<DomainName, List<UpstreamTarget>> _upstreams = new();
    private readonly Dictionary<DomainName, CertificateInfo> _certificates = new();

    public ProxySettings Settings { get; private set; } = ProxySettings.Default;

    public IReadOnlyDictionary<DomainName, DomainConfig> Domains => _domains;
    public IReadOnlyDictionary<DomainName, IReadOnlyList<UpstreamTarget>> Upstreams =>
        _upstreams.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<UpstreamTarget>)kvp.Value);
    public IReadOnlyDictionary<DomainName, CertificateInfo> Certificates => _certificates;
    public int EventCount { get; private set; }

    public bool HasDomain(DomainName domainName) => _domains.ContainsKey(domainName);

    public bool HasUpstream(DomainName domainName, UpstreamUrl url)
    {
        return _upstreams.TryGetValue(domainName, out var targets)
            && targets.Any(t => t.Url.Equals(url));
    }

    public UpstreamTarget? FindUpstreamByUrl(UpstreamUrl url)
    {
        foreach (var targets in _upstreams.Values)
        {
            var match = targets.FirstOrDefault(t => t.Url.Equals(url));
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    public void Apply(DomainAdded evt)
    {
        _domains[evt.Config.DomainName] = evt.Config;
        _upstreams[evt.Config.DomainName] = new List<UpstreamTarget>();
        EventCount++;
    }

    public void Apply(DomainUpdated evt)
    {
        _domains[evt.Config.DomainName] = evt.Config;
        EventCount++;
    }

    public void Apply(DomainRemoved evt)
    {
        _domains.Remove(evt.DomainName);
        _upstreams.Remove(evt.DomainName);
        _certificates.Remove(evt.DomainName);
        EventCount++;
    }

    public void Apply(UpstreamAdded evt)
    {
        if (_upstreams.TryGetValue(evt.DomainName, out var targets))
        {
            targets.Add(evt.Upstream);
        }

        EventCount++;
    }

    public void Apply(UpstreamRemoved evt)
    {
        if (_upstreams.TryGetValue(evt.DomainName, out var targets))
        {
            targets.RemoveAll(t => t.Url.Equals(evt.UpstreamUrl));
        }

        EventCount++;
    }

    public void Apply(SettingsUpdated evt)
    {
        Settings = evt.Settings;
        EventCount++;
    }

    /// <summary>
    /// Creates a snapshot of the current state.
    /// </summary>
    public ConfigurationSnapshot ToSnapshot()
    {
        return new ConfigurationSnapshot
        {
            Domains = _domains.Values.ToList(),
            Upstreams = _upstreams.ToDictionary(
                kvp => kvp.Key.Value,
                kvp => (IReadOnlyList<UpstreamTarget>)kvp.Value.ToList()),
            Certificates = _certificates.ToDictionary(
                kvp => kvp.Key.Value,
                kvp => kvp.Value),
            Settings = Settings,
        };
    }

    /// <summary>
    /// Restores state from a snapshot.
    /// </summary>
    public void RestoreFromSnapshot(ConfigurationSnapshot snapshot)
    {
        _domains.Clear();
        _upstreams.Clear();
        _certificates.Clear();

        foreach (var domain in snapshot.Domains)
        {
            _domains[domain.DomainName] = domain;
            _upstreams[domain.DomainName] = new List<UpstreamTarget>();
        }

        foreach (var kvp in snapshot.Upstreams)
        {
            var domainName = DomainName.Parse(kvp.Key);
            if (_upstreams.ContainsKey(domainName))
            {
                _upstreams[domainName] = kvp.Value.ToList();
            }
        }

        foreach (var kvp in snapshot.Certificates)
        {
            var domainName = DomainName.Parse(kvp.Key);
            _certificates[domainName] = kvp.Value;
        }

        Settings = snapshot.Settings;
        EventCount = 0;
    }
}

/// <summary>
/// Serializable snapshot of configuration state for faster recovery.
/// Uses string keys for domain names to ensure stable serialization.
/// </summary>
public sealed record ConfigurationSnapshot
{
    public required IReadOnlyList<DomainConfig> Domains { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<UpstreamTarget>> Upstreams { get; init; }
    public required IReadOnlyDictionary<string, CertificateInfo> Certificates { get; init; }
    public required ProxySettings Settings { get; init; }
}
