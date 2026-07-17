using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.SignalR;

/// <summary>
/// Relays <see cref="NotificationDeliveryEto"/> to connected SignalR users. Recipients receive only a
/// <see cref="NotificationDelivery"/>, which by construction omits the ETO's full recipient list — so one user
/// can never see the ids of the others (fixes the reference implementation's payload leak).
/// </summary>
[ExposeServices(
    typeof(INotificationNotifier),
    typeof(INotificationDeliveryNotifier),
    typeof(INotificationNotifier<NotificationDeliveryEto>),
    typeof(SignalRNotifier))]
public class SignalRNotifier :
    INotificationNotifier<NotificationDeliveryEto>,
    INotificationDeliveryNotifier,
    ITransientDependency
{
    public const string ChannelName = "SignalR";

    protected IHubContext<NotificationsHub, INotificationsClient> HubContext { get; }

    public string Name => ChannelName;

    public SignalRNotifier(IHubContext<NotificationsHub, INotificationsClient> hubContext)
    {
        HubContext = hubContext;
    }

    public virtual Task HandleEventAsync(NotificationDeliveryEto eventData)
    {
        // Skip when channel routing excludes SignalR, or there are no recipients.
        if (!NotificationChannels.IsAllowed(eventData.Channels, Name)
            || eventData.UserIds == null
            || eventData.UserIds.Length == 0)
        {
            return Task.CompletedTask;
        }

        // Trim the recipient list out of the payload, then deliver the same trimmed payload to every target user.
        var payload = NotificationDelivery.FromEto(eventData);
        var userIds = eventData.UserIds.Select(userId => userId.ToString()).ToList();

        return HubContext.Clients.Users(userIds).ReceiveNotification(payload);
    }

    public virtual async Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryWorkEto workItem)
    {
        if (!string.Equals(workItem.Channel, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {nameof(SignalRNotifier)} cannot deliver channel '{workItem.Channel}'.");
        }

        await HubContext.Clients
            .User(workItem.UserId.ToString())
            .ReceiveNotification(NotificationDelivery.FromWorkItem(workItem));
        return NotificationDeliveryResult.Succeeded();
    }
}
