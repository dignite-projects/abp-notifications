using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// AssemblyQualifiedName. Durable reads use its additive tolerant-reader capability so one historical payload
/// cannot fail a whole inbox page. Queries use proper joins/indexes (fixes roadmap problem D).
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(INotificationStore))]
public class NotificationStore : INotificationStore, IBatchedNotificationStore, ITransientDependency
{
    protected IRepository<Notification, Guid> NotificationRepository { get; }

    protected IRepository<UserNotification, Guid> UserNotificationRepository { get; }

    protected IRepository<NotificationSubscription, Guid> SubscriptionRepository { get; }

    protected INotificationDataSerializer DataSerializer { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected IClock Clock { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected IAsyncQueryableExecuter AsyncExecuter { get; }

    protected INotificationBatchPersistence BatchPersistence { get; }

    public NotificationStore(
        IRepository<Notification, Guid> notificationRepository,
        IRepository<UserNotification, Guid> userNotificationRepository,
        IRepository<NotificationSubscription, Guid> subscriptionRepository,
        INotificationDataSerializer dataSerializer,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter)
        : this(
            notificationRepository,
            userNotificationRepository,
            subscriptionRepository,
            dataSerializer,
            guidGenerator,
            clock,
            currentTenant,
            asyncExecuter,
            new NotificationBatchPersistence(userNotificationRepository))
    {
    }

    public NotificationStore(
        IRepository<Notification, Guid> notificationRepository,
        IRepository<UserNotification, Guid> userNotificationRepository,
        IRepository<NotificationSubscription, Guid> subscriptionRepository,
        INotificationDataSerializer dataSerializer,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter,
        INotificationBatchPersistence batchPersistence)
    {
        NotificationRepository = notificationRepository;
        UserNotificationRepository = userNotificationRepository;
        SubscriptionRepository = subscriptionRepository;
        DataSerializer = dataSerializer;
        GuidGenerator = guidGenerator;
        Clock = clock;
        CurrentTenant = currentTenant;
        AsyncExecuter = asyncExecuter;
        BatchPersistence = batchPersistence;
    }

    public virtual async Task InsertNotificationAsync(NotificationInfo notification)
    {
        await InsertNotificationAsync(notification, CancellationToken.None);
    }

    public virtual async Task InsertNotificationAsync(
        NotificationInfo notification,
        CancellationToken cancellationToken)
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

        await NotificationRepository.InsertAsync(entity, cancellationToken: cancellationToken);
    }

    public virtual async Task InsertUserNotificationAsync(UserNotificationInfo userNotification)
    {
        if (await UserNotificationExistsAsync(
                userNotification.UserId,
                userNotification.NotificationId,
                CancellationToken.None))
        {
            return;
        }

        var entity = new UserNotification(
            userNotification.Id == Guid.Empty ? GuidGenerator.Create() : userNotification.Id,
            userNotification.UserId,
            userNotification.NotificationId,
            userNotification.State,
            userNotification.CreationTime == default ? Clock.Now : userNotification.CreationTime,
            userNotification.TenantId ?? CurrentTenant.Id);

        await CancelRetentionDeletionAsync(entity.NotificationId, entity.TenantId, CancellationToken.None);
        await UserNotificationRepository.InsertAsync(entity);
    }

    public virtual async Task InsertUserNotificationsAsync(
        IReadOnlyCollection<UserNotificationInfo> userNotifications,
        CancellationToken cancellationToken = default)
    {
        if (userNotifications.Count == 0)
        {
            return;
        }

        var normalizedUserNotifications = userNotifications
            .GroupBy(userNotification => new { userNotification.UserId, userNotification.NotificationId })
            .Select(group => group.First())
            .ToList();
        var userIds = normalizedUserNotifications.Select(userNotification => userNotification.UserId).Distinct().ToList();
        var notificationIds = normalizedUserNotifications
            .Select(userNotification => userNotification.NotificationId)
            .Distinct()
            .ToList();
        var existingQuery = await UserNotificationRepository.GetQueryableAsync();
        var existingRows = await AsyncExecuter.ToListAsync(
            existingQuery
                .Where(row => userIds.Contains(row.UserId) && notificationIds.Contains(row.NotificationId))
                .Select(row => new { row.UserId, row.NotificationId }),
            cancellationToken);
        var existingKeys = existingRows
            .Select(row => (row.UserId, row.NotificationId))
            .ToHashSet();

        var entities = normalizedUserNotifications
            .Where(userNotification => !existingKeys.Contains((
                userNotification.UserId,
                userNotification.NotificationId)))
            .Select(userNotification => new UserNotification(
                userNotification.Id == Guid.Empty ? GuidGenerator.Create() : userNotification.Id,
                userNotification.UserId,
                userNotification.NotificationId,
                userNotification.State,
                userNotification.CreationTime == default ? Clock.Now : userNotification.CreationTime,
                userNotification.TenantId ?? CurrentTenant.Id))
            .ToList();
        if (entities.Count == 0)
        {
            return;
        }

        foreach (var group in entities
                     .GroupBy(entity => new { entity.TenantId, entity.NotificationId }))
        {
            await CancelRetentionDeletionAsync(group.Key.NotificationId, group.Key.TenantId, cancellationToken);
        }

        await BatchPersistence.InsertAsync(entities, cancellationToken);
    }

    protected virtual async Task<bool> UserNotificationExistsAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var query = await UserNotificationRepository.GetQueryableAsync();
        return await AsyncExecuter.AnyAsync(
            query.Where(row => row.UserId == userId && row.NotificationId == notificationId),
            cancellationToken);
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
        var tenantKey = NotificationSubscriptionIdentity.GetTenantKey(CurrentTenant.Id);
        var notificationNameKey = NotificationSubscriptionIdentity.GetNotificationNameKey(notificationName);
        var scopeKey = NotificationSubscriptionIdentity.GetScopeKey(entityTypeName, entityId);

        await SubscriptionRepository.DeleteAsync(x =>
            x.TenantKey == tenantKey && x.UserId == userId
            && x.NotificationNameKey == notificationNameKey && x.ScopeKey == scopeKey);
    }

    public virtual async Task<bool> IsSubscribedAsync(
        Guid userId, string notificationName, string? entityTypeName, string? entityId)
    {
        var tenantKey = NotificationSubscriptionIdentity.GetTenantKey(CurrentTenant.Id);
        var notificationNameKey = NotificationSubscriptionIdentity.GetNotificationNameKey(notificationName);
        var scopeKey = NotificationSubscriptionIdentity.GetScopeKey(entityTypeName, entityId);
        var query = await SubscriptionRepository.GetQueryableAsync();
        return await AsyncExecuter.AnyAsync(query.Where(x =>
            x.TenantKey == tenantKey && x.UserId == userId
            && x.NotificationNameKey == notificationNameKey && x.ScopeKey == scopeKey));
    }

    public virtual async Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(
        string notificationName, string? entityTypeName, string? entityId)
    {
        var tenantKey = NotificationSubscriptionIdentity.GetTenantKey(CurrentTenant.Id);
        var notificationNameKey = NotificationSubscriptionIdentity.GetNotificationNameKey(notificationName);
        var requestedScopeKey = NotificationSubscriptionIdentity.GetScopeKey(entityTypeName, entityId);
        var definitionWideScopeKey = NotificationSubscriptionIdentity.GetScopeKey(null, null);

        var entities = entityTypeName == null
            ? await SubscriptionRepository.GetListAsync(x =>
                x.TenantKey == tenantKey && x.NotificationNameKey == notificationNameKey
                && x.ScopeKey == definitionWideScopeKey)
            : await SubscriptionRepository.GetListAsync(x =>
                x.TenantKey == tenantKey && x.NotificationNameKey == notificationNameKey
                && (x.ScopeKey == definitionWideScopeKey || x.ScopeKey == requestedScopeKey));

        return entities.Select(MapToSubscriptionInfo).ToList();
    }

    public virtual async Task<List<Guid>> GetSubscriptionUserIdsAsync(
        string notificationName,
        string? entityTypeName,
        string? entityId,
        Guid? afterUserId,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResultCount);

        var tenantKey = NotificationSubscriptionIdentity.GetTenantKey(CurrentTenant.Id);
        var notificationNameKey = NotificationSubscriptionIdentity.GetNotificationNameKey(notificationName);
        var requestedScopeKey = NotificationSubscriptionIdentity.GetScopeKey(entityTypeName, entityId);
        var definitionWideScopeKey = NotificationSubscriptionIdentity.GetScopeKey(null, null);
        var query = await SubscriptionRepository.GetQueryableAsync();
        query = entityTypeName == null
            ? query.Where(subscription =>
                subscription.TenantKey == tenantKey &&
                subscription.NotificationNameKey == notificationNameKey &&
                subscription.ScopeKey == definitionWideScopeKey)
            : query.Where(subscription =>
                subscription.TenantKey == tenantKey &&
                subscription.NotificationNameKey == notificationNameKey &&
                (subscription.ScopeKey == definitionWideScopeKey ||
                 subscription.ScopeKey == requestedScopeKey));

        var recipientQuery = query.Select(subscription => subscription.UserId).Distinct();
        if (afterUserId.HasValue)
        {
            var cursor = afterUserId.Value;
            recipientQuery = recipientQuery.Where(userId => userId.CompareTo(cursor) > 0);
        }

        var recipientPage = recipientQuery
            .OrderBy(userId => userId)
            .Take(maxResultCount);

        return await AsyncExecuter.ToListAsync(recipientPage, cancellationToken);
    }

    public virtual async Task<List<NotificationSubscriptionInfo>> GetSubscriptionsAsync(Guid userId)
    {
        var tenantKey = NotificationSubscriptionIdentity.GetTenantKey(CurrentTenant.Id);
        var entities = await SubscriptionRepository.GetListAsync(x =>
            x.TenantKey == tenantKey && x.UserId == userId);
        return entities.Select(MapToSubscriptionInfo).ToList();
    }

    protected virtual NotificationInfo MapToNotificationInfo(Notification n)
    {
        return new NotificationInfo
        {
            Id = n.Id,
            NotificationName = n.NotificationName,
            Data = DeserializeDurableData(n.Data),
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

    protected virtual NotificationData? DeserializeDurableData(string? json)
    {
        if (DataSerializer is INotificationDataTolerantReader tolerantReader)
        {
            return tolerantReader.DeserializeTolerantly(json);
        }

        try
        {
            return DataSerializer.Deserialize(json);
        }
        catch (NotificationDataReadException exception)
        {
            return new UnsupportedNotificationData
            {
                OriginalDiscriminator = exception.Discriminator,
                OriginalSchemaVersion = exception.SchemaVersion,
                Reason = exception.Reason,
                RawJson = json ?? string.Empty
            };
        }
        catch (Exception exception) when (IsRecoverableReadException(exception))
        {
            return new UnsupportedNotificationData
            {
                Reason = UnsupportedNotificationDataReason.MalformedPayload,
                RawJson = json ?? string.Empty
            };
        }
    }

    protected virtual async Task CancelRetentionDeletionAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var notification = await NotificationRepository.FirstOrDefaultAsync(
            entity => entity.Id == notificationId && entity.TenantId == tenantId,
            cancellationToken: cancellationToken);
        if (notification?.RetentionDeletionTime == null)
        {
            return;
        }

        notification.CancelRetentionDeletion();
        await NotificationRepository.UpdateAsync(notification, autoSave: true, cancellationToken: cancellationToken);
    }

    private static bool IsRecoverableReadException(Exception exception)
    {
        return exception is not OperationCanceledException &&
               exception is not OutOfMemoryException &&
               exception is not StackOverflowException &&
               exception is not AccessViolationException &&
               exception is not AppDomainUnloadedException &&
               exception is not BadImageFormatException &&
               exception is not CannotUnloadAppDomainException &&
               exception is not InvalidProgramException;
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
