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
