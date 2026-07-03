using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationDistributer : INotificationDistributer, ITransientDependency
{
    protected INotificationStore Store { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected IDistributedEventBus DistributedEventBus { get; }

    public DefaultNotificationDistributer(
        INotificationStore store,
        INotificationDefinitionManager definitionManager,
        IDistributedEventBus distributedEventBus)
    {
        Store = store;
        DefinitionManager = definitionManager;
        DistributedEventBus = distributedEventBus;
    }

    public virtual async Task DistributeAsync(
        NotificationInfo notification, Guid[]? userIds = null, Guid[]? excludedUserIds = null)
    {
        var targetUserIds = await GetTargetUserIdsAsync(notification, userIds, excludedUserIds);
        if (targetUserIds.Count == 0)
        {
            return;
        }

        // NOTE: store-write then event-publish is not yet atomic. The ABP Outbox will make it so (roadmap P1).
        await SaveUserNotificationsAsync(notification, targetUserIds);
        await PublishRealTimeNotifyAsync(notification, targetUserIds);
    }

    protected virtual async Task<List<Guid>> GetTargetUserIdsAsync(
        NotificationInfo notification, Guid[]? userIds, Guid[]? excludedUserIds)
    {
        List<Guid> result;

        if (userIds != null && userIds.Length > 0)
        {
            // Explicitly targeted: honor the caller's list as-is (no subscription/availability filtering).
            result = userIds.Distinct().ToList();
        }
        else
        {
            // Subscription-driven: resolve subscribers and keep only those the notification is available to.
            var subscriptions = await Store.GetSubscriptionsAsync(
                notification.NotificationName, notification.EntityTypeName, notification.EntityId);

            result = new List<Guid>();
            foreach (var userId in subscriptions.Select(s => s.UserId).Distinct())
            {
                if (await DefinitionManager.IsAvailableAsync(notification.NotificationName, userId))
                {
                    result.Add(userId);
                }
            }
        }

        if (excludedUserIds != null && excludedUserIds.Length > 0)
        {
            var excluded = new HashSet<Guid>(excludedUserIds);
            result = result.Where(u => !excluded.Contains(u)).ToList();
        }

        return result;
    }

    protected virtual async Task SaveUserNotificationsAsync(NotificationInfo notification, List<Guid> userIds)
    {
        await Store.InsertNotificationAsync(notification);

        foreach (var userId in userIds)
        {
            await Store.InsertUserNotificationAsync(new UserNotificationInfo
            {
                UserId = userId,
                NotificationId = notification.Id,
                State = UserNotificationState.Unread,
                CreationTime = notification.CreationTime,
                TenantId = notification.TenantId
            });
        }
    }

    protected virtual Task PublishRealTimeNotifyAsync(NotificationInfo notification, List<Guid> userIds)
    {
        // The ETO carries the full recipient list for notifier routing; notifiers trim it per user before pushing.
        var eto = new RealTimeNotifyEto(
            notification.Id,
            notification.NotificationName,
            notification.Data,
            notification.Severity,
            notification.CreationTime,
            userIds.ToArray());

        return DistributedEventBus.PublishAsync(eto);
    }
}
