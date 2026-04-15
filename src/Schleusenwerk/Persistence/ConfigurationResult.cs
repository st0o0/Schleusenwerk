namespace Schleusenwerk.Persistence;

/// <summary>
/// Discriminated result type for configuration operations.
/// Replaces exception-based error handling with explicit success/failure paths.
/// </summary>
public abstract record ConfigurationResult
{
    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;

    public sealed record Success : ConfigurationResult
    {
        public static Success Instance { get; } = new();
    }

    public sealed record Failure(string Error) : ConfigurationResult;
}

/// <summary>
/// Discriminated result type with a value for query operations.
/// </summary>
public abstract record ConfigurationResult<T>
{
    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;

    public sealed record Success(T Value) : ConfigurationResult<T>;

    public sealed record Failure(string Error) : ConfigurationResult<T>;
}
