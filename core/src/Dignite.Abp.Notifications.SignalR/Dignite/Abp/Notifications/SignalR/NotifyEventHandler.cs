using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications.SignalR;

/// <summary>
/// Relays <see cref="RealTimeNotifyEto"/> to connected SignalR users. Recipients receive only a
/// <see cref="RealTimeNotification"/>, which by construction omits the ETO's full recipient list — so one user
/// can never see the ids of the others (fixes the reference implementation's payload leak).
/// </summary>
public class NotifyEventHandler : IDistributedEventHandler<RealTimeNotifyEto>, ITransientDependency
{
    protected IHubContext<NotificationsHub, INotificationClient> HubContext { get; }

    public NotifyEventHandler(IHubContext<NotificationsHub, INotificationClient> hubContext)
    {
        HubContext = hubContext;
    }

    public virtual Task HandleEventAsync(RealTimeNotifyEto eventData)
    {
        if (eventData.UserIds == null || eventData.UserIds.Length == 0)
        {
            return Task.CompletedTask;
        }

        // Trim the recipient list out of the payload, then deliver the same trimmed payload to every target user.
        var payload = RealTimeNotification.FromEto(eventData);
        var userIds = eventData.UserIds.Select(userId => userId.ToString()).ToList();

        return HubContext.Clients.Users(userIds).ReceiveNotification(payload);
    }
}
