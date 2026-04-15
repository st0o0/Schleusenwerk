namespace Schleusenwerk.Persistence;

/// <summary>
/// ACME certificate mode, controlled by the STAGE environment variable.
/// Mirrors the https-portal convention.
/// </summary>
public enum AcmeStage
{
    Local,
    Staging,
    Production,
}
