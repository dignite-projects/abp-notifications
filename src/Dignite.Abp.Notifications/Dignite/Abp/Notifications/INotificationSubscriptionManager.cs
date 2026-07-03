using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

public interface INotificationSubscriptionManager
{
    Task SubscribeAsync(Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null);

    Task SubscribeToAllAvailableNotificationsAsync(Guid userId);

    Task UnsubscribeAsync(Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null);

    Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(
        string notificationName, NotificationEntityIdentifier? entityIdentifier = null);

    Task<List<NotificationSubscriptionInfo>> GetSubscribedNotificationsAsync(Guid userId);

    Task<bool> IsSubscribedAsync(
        Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null);
}
