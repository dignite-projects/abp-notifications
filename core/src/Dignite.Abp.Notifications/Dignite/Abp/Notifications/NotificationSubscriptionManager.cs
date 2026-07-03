using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

public class NotificationSubscriptionManager : INotificationSubscriptionManager, ITransientDependency
{
    protected INotificationStore Store { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected IClock Clock { get; }

    public NotificationSubscriptionManager(
        INotificationStore store,
        INotificationDefinitionManager definitionManager,
        IClock clock)
    {
        Store = store;
        DefinitionManager = definitionManager;
        Clock = clock;
    }

    public virtual Task SubscribeAsync(
        Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null)
    {
        var (entityTypeName, entityId) = Deconstruct(entityIdentifier);
        return Store.InsertSubscriptionAsync(new NotificationSubscriptionInfo
        {
            UserId = userId,
            NotificationName = notificationName,
            EntityTypeName = entityTypeName,
            EntityId = entityId,
            CreationTime = Clock.Now
        });
    }

    public virtual async Task SubscribeToAllAvailableNotificationsAsync(Guid userId)
    {
        var definitions = await DefinitionManager.GetAllAvailableAsync(userId);
        foreach (var definition in definitions)
        {
            if (!await IsSubscribedAsync(userId, definition.Name))
            {
                await SubscribeAsync(userId, definition.Name);
            }
        }
    }

    public virtual Task UnsubscribeAsync(
        Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null)
    {
        var (entityTypeName, entityId) = Deconstruct(entityIdentifier);
        return Store.DeleteSubscriptionAsync(userId, notificationName, entityTypeName, entityId);
    }

    public virtual Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(
        string notificationName, NotificationEntityIdentifier? entityIdentifier = null)
    {
        var (entityTypeName, entityId) = Deconstruct(entityIdentifier);
        return Store.GetSubscriptionsAsync(notificationName, entityTypeName, entityId);
    }

    public virtual Task<List<NotificationSubscriptionInfo>> GetSubscribedNotificationsAsync(Guid userId)
    {
        return Store.GetSubscriptionsAsync(userId);
    }

    public virtual Task<bool> IsSubscribedAsync(
        Guid userId, string notificationName, NotificationEntityIdentifier? entityIdentifier = null)
    {
        var (entityTypeName, entityId) = Deconstruct(entityIdentifier);
        return Store.IsSubscribedAsync(userId, notificationName, entityTypeName, entityId);
    }

    protected static (string? EntityTypeName, string? EntityId) Deconstruct(NotificationEntityIdentifier? identifier)
    {
        return identifier == null
            ? (null, null)
            : (identifier.EntityType.FullName, identifier.EntityId.ToString());
    }
}
