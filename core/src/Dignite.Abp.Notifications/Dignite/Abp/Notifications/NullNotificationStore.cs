using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

/// <summary>
/// No-op store used in stateless forwarding mode (no NotificationCenter installed). Notifications are still
/// published as real-time events; nothing is persisted, and there are no subscriptions or inbox.
/// </summary>
public class NullNotificationStore : INotificationStore, ISingletonDependency
{
    public Task InsertSubscriptionAsync(NotificationSubscriptionInfo subscription) => Task.CompletedTask;

    public Task DeleteSubscriptionAsync(Guid userId, string notificationName, string? entityTypeName, string? entityId)
        => Task.CompletedTask;

    public Task<bool> IsSubscribedAsync(Guid userId, string notificationName, string? entityTypeName, string? entityId)
        => Task.FromResult(false);

    public Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(
        string notificationName, string? entityTypeName, string? entityId)
        => Task.FromResult(new List<NotificationSubscriptionInfo>());

    public Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(Guid userId)
        => Task.FromResult(new List<NotificationSubscriptionInfo>());

    public Task InsertNotificationAsync(NotificationInfo notification) => Task.CompletedTask;

    public Task InsertUserNotificationAsync(UserNotificationInfo userNotification) => Task.CompletedTask;

    public Task UpdateUserNotificationStateAsync(Guid userId, Guid notificationId, UserNotificationState state)
        => Task.CompletedTask;

    public Task UpdateAllUserNotificationStatesAsync(Guid userId, UserNotificationState state) => Task.CompletedTask;

    public Task DeleteUserNotificationAsync(Guid userId, Guid notificationId) => Task.CompletedTask;

    public Task DeleteAllUserNotificationsAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null)
        => Task.CompletedTask;

    public Task<List<UserNotificationWithNotification>> GetUserNotificationsAsync(
        Guid userId,
        UserNotificationState? state = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        DateTime? startDate = null,
        DateTime? endDate = null)
        => Task.FromResult(new List<UserNotificationWithNotification>());

    public Task<int> GetUserNotificationCountAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null)
        => Task.FromResult(0);
}
