using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationDistributor : INotificationDistributor, ITransientDependency
{
    protected INotificationStore Store { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected IDistributedEventBus DistributedEventBus { get; }

    public DefaultNotificationDistributor(
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
        // An empty, explicitly supplied recipient list is intentionally different from null. Return before
        // subscription lookup or channel validation so every direct/background path remains a true no-op.
        if (userIds is { Length: 0 })
        {
            return;
        }

        var channels = ResolveExternalChannelsOrNull(notification.NotificationName);
        var targetUserIds = await GetTargetUserIdsAsync(notification, userIds, excludedUserIds);
        if (targetUserIds.Count == 0)
        {
            return;
        }

        // These two commit together only when the host enables ABP's transactional outbox — with NotificationCenter
        // on EF Core that is Configure<AbpDistributedEventBusOptions>(o => o.UseNotificationCenterEfCoreOutbox()).
        // Without it, and always on the MongoDB provider (which wires no outbox), a crash between the two keeps the
        // rows and drops the event. Notification_Outbox_Tests covers the case where the guarantee does hold.
        await SaveUserNotificationsAsync(notification, targetUserIds);
        if (channels != null)
        {
            await PublishNotificationDeliveryAsync(notification, targetUserIds, channels);
        }
    }

    protected virtual string[]? ResolveExternalChannelsOrNull(string notificationName)
    {
        var definition = DefinitionManager.Get(notificationName);

        var channels = definition.GetChannelsOrNull();
        if (channels == null)
        {
            if (Store is NullNotificationStore)
            {
                throw new AbpException(
                    $"Notification '{notificationName}' has no external channels and no NotificationCenter inbox store is installed. Configure UseChannels(...) or install NotificationCenter.");
            }

            return null;
        }

        if (channels.Length == 0 || channels.Any(string.IsNullOrWhiteSpace))
        {
            throw new AbpException(
                $"Notification '{notificationName}' has invalid delivery channel configuration.");
        }

        return channels;
    }

    protected virtual async Task<List<Guid>> GetTargetUserIdsAsync(
        NotificationInfo notification, Guid[]? userIds, Guid[]? excludedUserIds)
    {
        List<Guid> result;

        if (userIds != null)
        {
            // Explicitly targeted: honor the caller's list (no subscription/availability filtering) while
            // preventing duplicate inbox rows and channel deliveries.
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

    protected virtual Task PublishNotificationDeliveryAsync(NotificationInfo notification, List<Guid> userIds, string[] channels)
    {
        // The ETO carries the full recipient list for notifier routing; notifiers trim it per user before pushing.
        var eto = new NotificationDeliveryEto(
            notification.Id,
            notification.NotificationName,
            notification.Data,
            notification.Severity,
            notification.CreationTime,
            userIds.ToArray())
        {
            Channels = channels,
            TenantId = notification.TenantId,
            EntityTypeName = notification.EntityTypeName,
            EntityId = notification.EntityId
        };

        return DistributedEventBus.PublishAsync(eto);
    }
}
