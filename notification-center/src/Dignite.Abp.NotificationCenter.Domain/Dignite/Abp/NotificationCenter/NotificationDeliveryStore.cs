using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Durable EF Core/MongoDB implementation of the core delivery-state abstraction.</summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(
    typeof(INotificationDeliveryStore),
    typeof(NotificationDeliveryStore))]
public class NotificationDeliveryStore :
    INotificationDeliveryStore,
    ITransientDependency
{
    protected IRepository<NotificationDeliveryRecord, Guid> DeliveryRepository { get; }
    protected IRepository<Notification, Guid>? NotificationRepository { get; }
    protected INotificationDataSerializer DataSerializer { get; }
    protected IGuidGenerator GuidGenerator { get; }
    protected IAsyncQueryableExecuter AsyncExecuter { get; }
    protected IDataFilter DataFilter { get; }
    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    public NotificationDeliveryStore(
        IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
        INotificationDataSerializer dataSerializer,
        IGuidGenerator guidGenerator,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IUnitOfWorkManager unitOfWorkManager)
    {
        DeliveryRepository = deliveryRepository;
        NotificationRepository = null;
        DataSerializer = dataSerializer;
        GuidGenerator = guidGenerator;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
        UnitOfWorkManager = unitOfWorkManager;
    }

    public NotificationDeliveryStore(
        IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
        IRepository<Notification, Guid> notificationRepository,
        INotificationDataSerializer dataSerializer,
        IGuidGenerator guidGenerator,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IUnitOfWorkManager unitOfWorkManager)
    {
        DeliveryRepository = deliveryRepository;
        NotificationRepository = notificationRepository;
        DataSerializer = dataSerializer;
        GuidGenerator = guidGenerator;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
        UnitOfWorkManager = unitOfWorkManager;
    }

    public virtual Task<NotificationDeliveryClaim?> EnsureCreatedAndTryClaimAsync(
        NotificationDeliveryRequestedEto workItem,
        DateTime now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workItem);
        return TryClaimCoreAsync(
            workItem,
            workItem.DeliveryId,
            workItem.TenantId,
            now,
            leaseDuration,
            maxAttempts,
            cancellationToken);
    }

    public virtual async Task EnsureCreatedAsync(
        NotificationDeliveryRequestedEto workItem,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(workItem);
        var existing = await DeliveryRepository.FindAsync(workItem.DeliveryId, cancellationToken: cancellationToken);
        if (existing != null)
        {
            EnsureSameIdentity(existing, workItem);
            await CancelRetentionDeletionAsync(
                workItem.NotificationId,
                workItem.TenantId,
                cancellationToken);
            return;
        }

        await CancelRetentionDeletionAsync(
            workItem.NotificationId,
            workItem.TenantId,
            cancellationToken);

        await DeliveryRepository.InsertAsync(
            CreateRecord(workItem),
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    public virtual Task<NotificationDeliveryClaim?> TryClaimAsync(
        Guid deliveryId,
        Guid? tenantId,
        DateTime now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        return TryClaimCoreAsync(
            workItem: null,
            deliveryId,
            tenantId,
            now,
            leaseDuration,
            maxAttempts,
            cancellationToken);
    }

    private async Task<NotificationDeliveryClaim?> TryClaimCoreAsync(
        NotificationDeliveryRequestedEto? workItem,
        Guid deliveryId,
        Guid? tenantId,
        DateTime now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var initialInsertInProgress = false;
        try
        {
            using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            using (DataFilter.Disable<IMultiTenant>())
            {
                var entity = await DeliveryRepository.FirstOrDefaultAsync(
                    delivery => delivery.Id == deliveryId && delivery.TenantId == tenantId,
                    cancellationToken: cancellationToken);
                if (entity == null)
                {
                    if (workItem == null)
                    {
                        await unitOfWork.CompleteAsync(cancellationToken);
                        return null;
                    }

                    await CancelRetentionDeletionAsync(
                        workItem.NotificationId,
                        workItem.TenantId,
                        cancellationToken);

                    entity = CreateRecord(workItem);
                    // Future quiet-hours work is independently committed as Pending and picked up by the retry
                    // worker at DeliveryNotBefore. Immediate work retains the one-INSERT Processing path.
                    var initialClaim = entity.CanBeClaimed(now)
                        ? entity.Claim(GuidGenerator.Create(), now, leaseDuration)
                        : null;
                    initialInsertInProgress = true;
                    await DeliveryRepository.InsertAsync(
                        entity,
                        autoSave: true,
                        cancellationToken: cancellationToken);
                    initialInsertInProgress = false;
                    await unitOfWork.CompleteAsync(cancellationToken);
                    return initialClaim;
                }

                if (workItem != null)
                {
                    EnsureSameIdentity(entity, workItem);
                    await CancelRetentionDeletionAsync(
                        workItem.NotificationId,
                        workItem.TenantId,
                        cancellationToken);
                }

                if (!entity.CanBeClaimed(now))
                {
                    await unitOfWork.CompleteAsync(cancellationToken);
                    return null;
                }

                if (entity.AttemptCount >= maxAttempts)
                {
                    entity.MarkAbandonedAsDeadLettered(now);
                    await UpdateProcessingDeliveryAsync(entity, cancellationToken);
                    await unitOfWork.CompleteAsync(cancellationToken);
                    return null;
                }

                var claim = entity.Claim(GuidGenerator.Create(), now, leaseDuration);
                await UpdateProcessingDeliveryAsync(entity, cancellationToken);
                await unitOfWork.CompleteAsync(cancellationToken);
                return claim;
            }
        }
        catch (AbpDbConcurrencyException)
        {
            return null;
        }
        catch (Exception) when (
            initialInsertInProgress
            && workItem != null
            && !cancellationToken.IsCancellationRequested)
        {
            // A concurrent first event can win the deterministic primary-key insert between our read and write.
            // Provider exceptions differ (for example EF DbUpdateException vs Mongo duplicate-key), so only treat
            // the failure as a lost claim after a fresh UOW proves that the same delivery identity now exists.
            // If no competitor row is visible, preserve the original provider failure for inbox redelivery.
            if (await SameDeliveryWasCommittedByCompetitorAsync(workItem, cancellationToken))
            {
                return null;
            }

            throw;
        }
    }

    public virtual Task<bool> MarkSucceededAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime completedAt,
        CancellationToken cancellationToken = default)
    {
        return UpdateTerminalAsync(
            deliveryId,
            tenantId,
            entity => entity.MarkSucceeded(leaseId, completedAt),
            cancellationToken);
    }

    public virtual Task<bool> MarkSuppressedAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime completedAt,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        return UpdateTerminalAsync(
            deliveryId,
            tenantId,
            entity => entity.MarkSuppressed(leaseId, completedAt, reasonCode),
            cancellationToken);
    }

    public virtual Task<bool> MarkFailedAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime failedAt,
        string failureCode,
        DateTime? nextAttemptTime,
        CancellationToken cancellationToken = default)
    {
        return UpdateTerminalAsync(
            deliveryId,
            tenantId,
            entity => entity.MarkFailed(
                leaseId,
                failedAt,
                failureCode,
                nextAttemptTime),
            cancellationToken);
    }

    public virtual async Task<IReadOnlyList<NotificationDeliveryRequestedEto>> GetDueWorkItemsAsync(
        DateTime now,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var deliveryQuery = await DeliveryRepository.GetQueryableAsync();
            var deliveries = await AsyncExecuter.ToListAsync(deliveryQuery
                .Where(delivery => delivery.State == NotificationDeliveryState.Pending
                                   && (!delivery.NextAttemptTime.HasValue || delivery.NextAttemptTime <= now)
                                   || delivery.State == NotificationDeliveryState.RetryScheduled
                                   && (!delivery.NextAttemptTime.HasValue || delivery.NextAttemptTime <= now)
                                   || delivery.State == NotificationDeliveryState.Processing
                                   && delivery.LeaseExpirationTime <= now)
                .OrderBy(delivery => delivery.CreationTime)
                .Take(maxResultCount), cancellationToken);
            if (deliveries.Count == 0)
            {
                await unitOfWork.CompleteAsync(cancellationToken);
                return Array.Empty<NotificationDeliveryRequestedEto>();
            }

            var items = deliveries
                .Select(ToWorkItem)
                .ToList();
            await unitOfWork.CompleteAsync(cancellationToken);
            return items;
        }
    }

    public virtual Task<bool> RetryAsync(
        Guid deliveryId,
        Guid? tenantId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        return UpdateTerminalAsync(
            deliveryId,
            tenantId,
            entity => entity.Retry(now),
            cancellationToken);
    }

    public virtual Task<bool> ForceDeliverAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid actorId,
        DateTime now,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        return UpdateTerminalAsync(
            deliveryId,
            tenantId,
            entity => entity.ForceDeliver(actorId, now, reasonCode),
            cancellationToken);
    }

    protected virtual NotificationData? DeserializeDurableData(string? json)
    {
        return DataSerializer.Deserialize(json, NotificationDataReadMode.Tolerant);
    }

    private async Task<bool> UpdateTerminalAsync(
        Guid deliveryId,
        Guid? tenantId,
        Func<NotificationDeliveryRecord, bool> transition,
        CancellationToken cancellationToken)
    {
        try
        {
            using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            using (DataFilter.Disable<IMultiTenant>())
            {
                var entity = await DeliveryRepository.FirstOrDefaultAsync(
                    delivery => delivery.Id == deliveryId && delivery.TenantId == tenantId,
                    cancellationToken: cancellationToken);
                if (entity == null || !transition(entity))
                {
                    await unitOfWork.CompleteAsync(cancellationToken);
                    return false;
                }

                await DeliveryRepository.UpdateAsync(entity, autoSave: true, cancellationToken: cancellationToken);
                await unitOfWork.CompleteAsync(cancellationToken);
                return true;
            }
        }
        catch (AbpDbConcurrencyException)
        {
            return false;
        }
    }

    private NotificationDeliveryRequestedEto ToWorkItem(NotificationDeliveryRecord delivery)
    {
        return new NotificationDeliveryRequestedEto
        {
            DeliveryId = delivery.Id,
            IdempotencyKey = delivery.IdempotencyKey,
            NotificationId = delivery.NotificationId,
            NotificationName = delivery.NotificationName,
            Data = DeserializeDurableData(delivery.Data),
            Severity = delivery.Severity,
            CreationTime = delivery.CreationTime,
            UserId = delivery.UserId,
            Channel = delivery.Channel,
            Intent = delivery.Intent,
            DeliveryNotBefore = delivery.DeliveryNotBefore,
            PreferenceReasonCode = delivery.PreferenceReasonCode,
            EntityTypeName = delivery.EntityTypeName,
            EntityId = delivery.EntityId,
            TenantId = delivery.TenantId
        };
    }

    protected virtual NotificationDeliveryRecord CreateRecord(NotificationDeliveryRequestedEto workItem)
    {
        return new NotificationDeliveryRecord(
            workItem.DeliveryId,
            workItem.NotificationId,
            workItem.UserId,
            workItem.Channel,
            workItem.IdempotencyKey,
            workItem.NotificationName,
            DataSerializer.Serialize(workItem.Data),
            workItem.EntityTypeName,
            workItem.EntityId,
            workItem.Severity,
            workItem.CreationTime,
            workItem.TenantId,
            workItem.Intent,
            workItem.DeliveryNotBefore,
            workItem.PreferenceReasonCode);
    }

    protected virtual Task<NotificationDeliveryRecord> UpdateProcessingDeliveryAsync(
        NotificationDeliveryRecord delivery,
        CancellationToken cancellationToken)
    {
        return DeliveryRepository.UpdateAsync(
            delivery,
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    protected virtual async Task CancelRetentionDeletionAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (NotificationRepository == null)
        {
            return;
        }

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

    private async Task<bool> SameDeliveryWasCommittedByCompetitorAsync(
        NotificationDeliveryRequestedEto workItem,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var existing = await DeliveryRepository.FirstOrDefaultAsync(
                delivery => delivery.Id == workItem.DeliveryId && delivery.TenantId == workItem.TenantId,
                cancellationToken: cancellationToken);
            if (existing == null)
            {
                await unitOfWork.CompleteAsync(cancellationToken);
                return false;
            }

            EnsureSameIdentity(existing, workItem);
            await unitOfWork.CompleteAsync(cancellationToken);
            return true;
        }
    }

    private static void ValidateIdentity(NotificationDeliveryRequestedEto workItem)
    {
        workItem.ValidateIntent();
        if (workItem.DeliveryId != NotificationDeliveryIdentity.CreateId(
                workItem.TenantId,
                workItem.NotificationId,
                workItem.UserId,
                workItem.Channel)
            || !string.Equals(
                workItem.IdempotencyKey,
                NotificationDeliveryIdentity.CreateIdempotencyKey(
                    workItem.TenantId,
                    workItem.NotificationId,
                    workItem.UserId,
                    workItem.Channel),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The notification delivery work-item identity is invalid.");
        }
    }

    private static void EnsureSameIdentity(
        NotificationDeliveryRecord existing,
        NotificationDeliveryRequestedEto workItem)
    {
        if (existing.TenantId != workItem.TenantId
            || existing.NotificationId != workItem.NotificationId
            || existing.UserId != workItem.UserId
            || !string.Equals(
                existing.ChannelKey,
                NotificationDeliveryIdentity.NormalizeChannel(workItem.Channel),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A notification delivery id was reused for another identity.");
        }
    }
}
