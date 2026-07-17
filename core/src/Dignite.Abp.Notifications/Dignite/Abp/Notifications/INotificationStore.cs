using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Persistence abstraction for notifications and subscriptions. The core depends only on this; the optional
/// NotificationCenter module supplies a real implementation, otherwise <see cref="NullNotificationStore"/> is used.
/// </summary>
public interface INotificationStore
{
    /// <summary>Inserts one definition-wide or entity-specific subscription identity.</summary>
    Task InsertSubscriptionAsync(NotificationSubscriptionInfo subscription);

    /// <summary>Deletes only the exact definition-wide or entity-specific subscription identity.</summary>
    Task DeleteSubscriptionAsync(Guid userId, string notificationName, string? entityTypeName, string? entityId);

    /// <summary>Checks only the exact definition-wide or entity-specific subscription identity.</summary>
    Task<bool> IsSubscribedAsync(Guid userId, string notificationName, string? entityTypeName, string? entityId);

    /// <summary>
    /// Gets recipients for a notification. A definition-wide notification matches definition-wide subscriptions;
    /// an entity notification matches the union of definition-wide subscriptions and subscriptions to that exact entity.
    /// </summary>
    Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(
        string notificationName, string? entityTypeName, string? entityId);

    /// <summary>Gets every distinct subscription identity stored for the user in the current tenant.</summary>
    Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(Guid userId);

    Task InsertNotificationAsync(NotificationInfo notification);

    Task InsertUserNotificationAsync(UserNotificationInfo userNotification);

    Task UpdateUserNotificationStateAsync(Guid userId, Guid notificationId, UserNotificationState state);

    Task UpdateAllUserNotificationStatesAsync(Guid userId, UserNotificationState state);

    Task DeleteUserNotificationAsync(Guid userId, Guid notificationId);

    Task DeleteAllUserNotificationsAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null);

    Task<List<UserNotificationWithNotification>> GetUserNotificationsAsync(
        Guid userId,
        UserNotificationState? state = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        DateTime? startDate = null,
        DateTime? endDate = null);

    Task<int> GetUserNotificationCountAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null);
}
