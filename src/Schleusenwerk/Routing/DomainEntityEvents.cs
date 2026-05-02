namespace Schleusenwerk.Routing;

public sealed record DomainConfigured(DomainConfig Config) : IDomainEvent;

public sealed record DomainUpstreamAdded(UpstreamTarget Target) : IDomainEvent;

public sealed record DomainUpstreamRemoved(UpstreamUrl Url) : IDomainEvent;

public sealed record DomainDeactivated(DomainName DomainName) : IDomainEvent;
