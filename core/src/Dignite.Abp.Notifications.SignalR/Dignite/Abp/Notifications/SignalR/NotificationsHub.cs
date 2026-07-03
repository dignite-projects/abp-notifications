using Volo.Abp.AspNetCore.SignalR;

namespace Dignite.Abp.Notifications.SignalR;

/// <summary>
/// SignalR hub clients connect to for real-time notifications. Delivery is push-only (server → client);
/// clients invoke no server methods here. Route mapping is wired up by the host (roadmap Increment 2+).
/// </summary>
public class NotificationsHub : AbpHub<INotificationClient>
{
}
