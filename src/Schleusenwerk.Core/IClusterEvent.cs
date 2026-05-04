namespace Schleusenwerk;

/// <summary>
/// Marker interface for events distributed cluster-wide through EventHubActor.
/// </summary>
public interface IClusterEvent;

public interface IDomainEvent : IClusterEvent;

public interface IUpstreamEvent : IClusterEvent;

public interface ICertificateEvent : IClusterEvent;
