namespace Schleusenwerk.Persistence;

/// <summary>
/// Thrown when a configuration command or query fails.
/// </summary>
public sealed class ConfigurationServiceException : Exception
{
    public ConfigurationServiceException(string message) : base(message)
    {
    }
}
