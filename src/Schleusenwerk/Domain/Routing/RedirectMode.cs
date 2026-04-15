namespace Schleusenwerk.Domain.Routing;

public enum RedirectMode
{
    None = 0,
    PermanentRedirect = 301,
    TemporaryRedirect = 307,
}
