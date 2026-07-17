using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

public interface INotificationSubscriptionManager
{
    /// <summary>
    /// Subscribes to all instances when <paramref name="entityIdentifier"/> is null, or to one exact entity otherwise.
    /// </summary>
    Task SubscribeAsync(Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null);

    /// <summary>Creates definition-wide subscriptions for every available notification definition.</summary>
    Task SubscribeToAllAvailableNotificationsAsync(Guid userId);

    /// <summary>Removes only the exact definition-wide or entity-specific subscription identity.</summary>
    Task UnsubscribeAsync(Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null);

    /// <summary>
    /// Resolves recipients using definition-wide fallback plus an exact entity match when an entity is supplied.
    /// </summary>
    Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(
        string notificationName, NotificationEntityIdentifier? entityIdentifier = null);

    /// <summary>Gets all definition-wide and entity-specific identities stored for a user.</summary>
    Task<List<NotificationSubscriptionInfo>> GetSubscribedNotificationsAsync(Guid userId);

    /// <summary>Checks only the exact definition-wide or entity-specific subscription identity.</summary>
    Task<bool> IsSubscribedAsync(
        Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null);
}
