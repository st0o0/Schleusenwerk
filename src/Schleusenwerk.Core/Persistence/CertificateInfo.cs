namespace Schleusenwerk.Persistence;

/// <summary>
/// Immutable certificate metadata for a domain.
/// </summary>
public sealed record CertificateInfo
{
    public required string Thumbprint { get; init; }
    public required DateTimeOffset NotBefore { get; init; }
    public required DateTimeOffset NotAfter { get; init; }
    public required string Issuer { get; init; }
    public bool IsSelfSigned { get; init; }

    public bool IsExpired(DateTimeOffset now) => now >= NotAfter;

    public bool IsExpiringSoon(DateTimeOffset now, TimeSpan threshold) =>
        now >= NotAfter - threshold;
}
