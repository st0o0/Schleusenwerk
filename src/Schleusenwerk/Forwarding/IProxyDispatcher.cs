namespace Schleusenwerk.Forwarding;

internal interface IProxyDispatcher
{
    Task HandleAsync(HttpContext context, CancellationToken ct);
}
