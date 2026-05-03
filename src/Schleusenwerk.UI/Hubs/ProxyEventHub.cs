using Microsoft.AspNetCore.SignalR;
using Schleusenwerk.Contracts;

namespace Schleusenwerk.UI.Hubs;

public sealed class ProxyEventHub : Hub
{
    public static Task BroadcastAsync(IHubContext<ProxyEventHub> context, ProxyEvent evt) =>
        context.Clients.All.SendAsync("OnProxyEvent", evt);
}
