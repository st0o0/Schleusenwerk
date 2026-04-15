namespace Schleusenwerk.Persistence;

/// <summary>
/// Global proxy settings that apply across all domains.
/// </summary>
public sealed record ProxySettings
{
    public TimeSpan DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxConnectionsPerUpstream { get; init; } = 100;
    public bool ForceHttpsGlobally { get; init; }
    public int SnapshotInterval { get; init; } = 100;
    public AcmeStage Stage { get; init; } = AcmeStage.Local;

    public static ProxySettings Default => new();
}
