using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// EF-backed implementation of the core <see cref="INotificationStore"/>. Replaces the framework's
/// <c>NullNotificationStore</c> when this module is installed. Both write and read go through the single core
/// <see cref="INotificationDataSerializer"/> (System.Text.Json + stable discriminator) — no Newtonsoft, no
/// AssemblyQualifiedName (fixes roadmap problem A). Queries use proper joins/indexes (fixes problem D).
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(INotificationStore))]
public class NotificationStore : INotificationStore, ITransientDependency
{
    protected IRepository<Notification, Guid> NotificationRepository { get; }

    protected IRepository<UserNotification, Guid> UserNotificationRepository { get; }

    protected IRepository<NotificationSubscription, Guid> SubscriptionRepository { get; }

    protected INotificationDataSerializer DataSerializer { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected IClock Clock { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected IAsyncQueryableExecuter AsyncExecuter { get; }

    public NotificationStore(
        IRepository<Notification, Guid> notificationRepository,
        IRepository<UserNotification, Guid> userNotificationRepository,
        IRepository<NotificationSubscription, Guid> subscriptionRepository,
        INotificationDataSerializer dataSerializer,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter)
    {
        NotificationRepository = notificationRepository;
        UserNotificationRepository = userNotificationRepository;
        SubscriptionRepository = subscriptionRepository;
        DataSerializer = dataSerializer;
        GuidGenerator = guidGenerator;
        Clock = clock;
        CurrentTenant = currentTenant;
        AsyncExecuter = asyncExecuter;
    }

    public virtual async Task InsertNotificationAsync(NotificationInfo notification)
    {
        var entity = new Notification(
            notification.Id,
            notification.NotificationName,
            DataSerializer.Serialize(notification.Data),
            notification.EntityTypeName,
            notification.EntityId,
            notification.Severity,
            notification.CreationTime,
            notification.TenantId ?? CurrentTenant.Id);

        await NotificationRepository.InsertAsync(entity);
    }

    public virtual async Task InsertUserNotificationAsync(UserNotificationInfo userNotification)
    {
        var entity = new UserNotification(
            userNotification.Id == Guid.Empty ? GuidGenerator.Create() : userNotification.Id,
            userNotification.UserId,
            userNotification.NotificationId,
            userNotification.State,
            userNotification.CreationTime == default ? Clock.Now : userNotification.CreationTime,
            userNotification.TenantId ?? CurrentTenant.Id);

        await UserNotificationRepository.InsertAsync(entity);
    }

    public virtual async Task UpdateUserNotificationStateAsync(Guid userId, Guid notificationId, UserNotificationState state)
    {
        var entity = await UserNotificationRepository.FirstOrDefaultAsync(
            x => x.UserId == userId && x.NotificationId == notificationId);

        if (entity != null)
        {
            entity.SetState(state);
            await UserNotificationRepository.UpdateAsync(entity);
        }
    }

    public virtual async Task UpdateAllUserNotificationStatesAsync(Guid userId, UserNotificationState state)
    {
        var entities = await UserNotificationRepository.GetListAsync(x => x.UserId == userId);
        foreach (var entity in entities)
        {
            entity.SetState(state);
        }

        await UserNotificationRepository.UpdateManyAsync(entities);
    }

    public virtual async Task DeleteUserNotificationAsync(Guid userId, Guid notificationId)
    {
        await UserNotificationRepository.DeleteAsync(x => x.UserId == userId && x.NotificationId == notificationId);
    }

    public virtual async Task DeleteAllUserNotificationsAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        await UserNotificationRepository.DeleteAsync(x =>
            x.UserId == userId
            && (state == null || x.State == state)
            && (startDate == null || x.CreationTime >= startDate)
            && (endDate == null || x.CreationTime <= endDate));
    }

    public virtual async Task<List<UserNotificationWithNotification>> GetUserNotificationsAsync(
        Guid userId,
        UserNotificationState? state = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        // Two indexed queries + an in-memory join, rather than a cross-collection join, so the SAME store works on
        // both EF Core and MongoDB. The (UserId, State, CreationTime) index serves the first query; the second is a
        // primary-key batch lookup. A user-notification whose notification was deleted is skipped, not thrown on
        // (roadmap problem D).
        var userNotificationQuery = await UserNotificationRepository.GetQueryableAsync();
        userNotificationQuery = userNotificationQuery.Where(un =>
            un.UserId == userId
            && (state == null || un.State == state)
            && (startDate == null || un.CreationTime >= startDate)
            && (endDate == null || un.CreationTime <= endDate));

        var pagedUserNotifications = await AsyncExecuter.ToListAsync(
            userNotificationQuery
                .OrderByDescending(un => un.CreationTime)
                .Skip(skipCount)
                .Take(maxResultCount));

        if (pagedUserNotifications.Count == 0)
        {
            return new List<UserNotificationWithNotification>();
        }

        var notificationIds = pagedUserNotifications.Select(un => un.NotificationId).Distinct().ToList();
        var notifications = await NotificationRepository.GetListAsync(n => notificationIds.Contains(n.Id));
        var notificationsById = notifications.ToDictionary(n => n.Id);

        var result = new List<UserNotificationWithNotification>();
        foreach (var userNotification in pagedUserNotifications)
        {
            if (notificationsById.TryGetValue(userNotification.NotificationId, out var notification))
            {
                result.Add(new UserNotificationWithNotification
                {
                    UserNotification = MapToUserNotificationInfo(userNotification),
                    Notification = MapToNotificationInfo(notification)
                });
            }
        }

        return result;
    }

    public virtual async Task<int> GetUserNotificationCountAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = await UserNotificationRepository.GetQueryableAsync();
        query = query.Where(un =>
            un.UserId == userId
            && (state == null || un.State == state)
            && (startDate == null || un.CreationTime >= startDate)
            && (endDate == null || un.CreationTime <= endDate));

        return await AsyncExecuter.CountAsync(query);
    }

    public virtual async Task InsertSubscriptionAsync(NotificationSubscriptionInfo subscription)
    {
        var entity = new NotificationSubscription(
            GuidGenerator.Create(),
            subscription.UserId,
            subscription.NotificationName,
            subscription.EntityTypeName,
            subscription.EntityId,
            subscription.CreationTime == default ? Clock.Now : subscription.CreationTime,
            subscription.TenantId ?? CurrentTenant.Id);

        await SubscriptionRepository.InsertAsync(entity);
    }

    public virtual async Task DeleteSubscriptionAsync(
        Guid userId, string notificationName, string? entityTypeName, string? entityId)
    {
        await SubscriptionRepository.DeleteAsync(x =>
            x.UserId == userId && x.NotificationName == notificationName
            && x.EntityTypeName == entityTypeName && x.EntityId == entityId);
    }

    public virtual async Task<bool> IsSubscribedAsync(
        Guid userId, string notificationName, string? entityTypeName, string? entityId)
    {
        var query = await SubscriptionRepository.GetQueryableAsync();
        return await AsyncExecuter.AnyAsync(query.Where(x =>
            x.UserId == userId && x.NotificationName == notificationName
            && x.EntityTypeName == entityTypeName && x.EntityId == entityId));
    }

    public virtual async Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(
        string notificationName, string? entityTypeName, string? entityId)
    {
        var entities = await SubscriptionRepository.GetListAsync(x =>
            x.NotificationName == notificationName && x.EntityTypeName == entityTypeName && x.EntityId == entityId);

        return entities.Select(MapToSubscriptionInfo).ToList();
    }

    public virtual async Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(Guid userId)
    {
        var entities = await SubscriptionRepository.GetListAsync(x => x.UserId == userId);
        return entities.Select(MapToSubscriptionInfo).ToList();
    }

    protected virtual NotificationInfo MapToNotificationInfo(Notification n)
    {
        return new NotificationInfo
        {
            Id = n.Id,
            NotificationName = n.NotificationName,
            Data = DataSerializer.Deserialize(n.Data),
            EntityTypeName = n.EntityTypeName,
            EntityId = n.EntityId,
            Severity = n.Severity,
            CreationTime = n.CreationTime,
            TenantId = n.TenantId
        };
    }

    protected virtual UserNotificationInfo MapToUserNotificationInfo(UserNotification un)
    {
        return new UserNotificationInfo
        {
            Id = un.Id,
            UserId = un.UserId,
            NotificationId = un.NotificationId,
            State = un.State,
            CreationTime = un.CreationTime,
            TenantId = un.TenantId
        };
    }

    protected virtual NotificationSubscriptionInfo MapToSubscriptionInfo(NotificationSubscription s)
    {
        return new NotificationSubscriptionInfo
        {
            UserId = s.UserId,
            NotificationName = s.NotificationName,
            EntityTypeName = s.EntityTypeName,
            EntityId = s.EntityId,
            CreationTime = s.CreationTime,
            TenantId = s.TenantId
        };
    }
}
