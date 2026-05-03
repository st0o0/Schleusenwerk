namespace Schleusenwerk.Routing;

public sealed record RequestFailed(UpstreamUrl Url) : IWithDomain
{
    public required string Domain { get; init; }
}

public sealed record RequestSucceeded(UpstreamUrl Url) : IWithDomain
{
    public required string Domain { get; init; }
}
