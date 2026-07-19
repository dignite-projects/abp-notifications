using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.SignalR;

/// <summary>
/// Relays reliable single-recipient work to connected SignalR users. Recipients receive only a
/// <see cref="NotificationDelivery"/>, which by construction omits every aggregate recipient list.
/// </summary>
[ExposeServices(
    typeof(INotificationNotifier),
    typeof(SignalRNotifier))]
public class SignalRNotifier :
    INotificationNotifier,
    ITransientDependency
{
    public const string ChannelName = "SignalR";

    protected IHubContext<NotificationsHub> HubContext { get; }

    public string Name => ChannelName;

    public SignalRNotifier(IHubContext<NotificationsHub> hubContext)
    {
        HubContext = hubContext;
    }

    public virtual async Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryRequestedEto workItem,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(workItem.Channel, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {nameof(SignalRNotifier)} cannot deliver channel '{workItem.Channel}'.");
        }

        await HubContext.Clients.User(workItem.UserId.ToString()).SendCoreAsync(
            nameof(INotificationsClient.ReceiveNotification),
            new object[] { NotificationDelivery.FromWorkItem(workItem) },
            cancellationToken);
        return NotificationDeliveryResult.Succeeded();
    }
}
