using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Dignite.Abp.NotificationCenter;

public class NotificationRetentionCleanupService :
    INotificationRetentionCleanupService,
    ITransientDependency
{
    protected IRepository<Notification, Guid> NotificationRepository { get; }
    protected IRepository<UserNotification, Guid> UserNotificationRepository { get; }
    protected IRepository<NotificationDeliveryRecord, Guid> DeliveryRepository { get; }
    protected IRepository<NotificationRetentionCleanupCursor, Guid> CleanupCursorRepository { get; }
    protected IAsyncQueryableExecuter AsyncExecuter { get; }
    protected IDataFilter DataFilter { get; }
    protected IClock Clock { get; }
    protected IUnitOfWorkManager UnitOfWorkManager { get; }
    protected IOptions<NotificationRetentionOptions> Options { get; }
    protected IReadOnlyList<INotificationRetentionDeletionContributor> DeletionContributors { get; }
    protected ILogger<NotificationRetentionCleanupService> Logger { get; }
    protected NotificationAudienceBroadcastStateRetentionCleaner? BroadcastStateRetentionCleaner { get; }

    public NotificationRetentionCleanupService(
        IRepository<Notification, Guid> notificationRepository,
        IRepository<UserNotification, Guid> userNotificationRepository,
        IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
        IRepository<NotificationRetentionCleanupCursor, Guid> cleanupCursorRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<NotificationRetentionOptions> options,
        IEnumerable<INotificationRetentionDeletionContributor> deletionContributors,
        ILogger<NotificationRetentionCleanupService> logger,
        NotificationAudienceBroadcastStateRetentionCleaner? broadcastStateRetentionCleaner = null)
    {
        NotificationRepository = notificationRepository;
        UserNotificationRepository = userNotificationRepository;
        DeliveryRepository = deliveryRepository;
        CleanupCursorRepository = cleanupCursorRepository;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
        Clock = clock;
        UnitOfWorkManager = unitOfWorkManager;
        Options = options;
        DeletionContributors = deletionContributors.ToArray();
        Logger = logger;
        BroadcastStateRetentionCleaner = broadcastStateRetentionCleaner;
    }

    public virtual async Task<NotificationRetentionCleanupResult> CleanupAsync(
        NotificationRetentionCleanupRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new NotificationRetentionCleanupRequest();
        var now = request.Now ?? Clock.Now;
        var batchSize = request.BatchSize ?? Options.Value.CleanupBatchSize;
        ValidateBatchSize(batchSize);

        var result = new NotificationRetentionCleanupResult
        {
            IsDryRun = request.IsDryRun,
            StartedTime = now
        };

        await CleanupUserNotificationsAsync(request, result, now, batchSize, cancellationToken);
        await CleanupDeliveriesAsync(request, result, now, batchSize, cancellationToken);
        if (BroadcastStateRetentionCleaner != null)
        {
            await BroadcastStateRetentionCleaner.CleanupAsync(
                request,
                result,
                now,
                batchSize,
                cancellationToken);
        }
        await CleanupNotificationsAsync(request, result, now, batchSize, cancellationToken);
        await SetOldestRetainedTimestampsAsync(request, result, cancellationToken);

        result.CompletedTime = Clock.Now;
        RecordMetrics(result);
        return result;
    }

    protected virtual async Task CleanupUserNotificationsAsync(
        NotificationRetentionCleanupRequest request,
        NotificationRetentionCleanupResult result,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!Options.Value.ReadUserNotificationRetention.HasValue)
        {
            return;
        }

        var cutoff = now - Options.Value.ReadUserNotificationRetention.Value;
        var cursor = await GetCleanupCursorAsync(
            NotificationRetentionRecordKind.UserNotification,
            request,
            cancellationToken);
        var afterCreationTime = cursor?.LastCreationTime;
        var afterId = cursor?.LastRecordId;
        var hasWrapped = false;
        var shouldResetCursor = false;
        var scannedBefore = result.ScannedUserNotifications;

        while (result.ScannedUserNotifications < batchSize)
        {
            var remainingBatchSize = (int)(batchSize - result.ScannedUserNotifications);
            var candidates = await GetUserNotificationCandidatesAsync(
                request,
                cutoff,
                remainingBatchSize,
                afterCreationTime,
                afterId,
                cancellationToken);
            if (candidates.Count == 0)
            {
                if (!hasWrapped &&
                    afterCreationTime.HasValue &&
                    afterId.HasValue &&
                    result.ScannedUserNotifications == scannedBefore)
                {
                    afterCreationTime = null;
                    afterId = null;
                    hasWrapped = true;
                    shouldResetCursor = true;
                    continue;
                }

                break;
            }

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.ScannedUserNotifications++;
                afterCreationTime = candidate.CreationTime;
                afterId = candidate.Id;

                try
                {
                    await ProcessUserNotificationCandidateAsync(
                        candidate,
                        cutoff,
                        request.IsDryRun,
                        result,
                        cancellationToken);
                }
                catch (Exception exception) when (IsRecoverableCleanupException(exception, cancellationToken))
                {
                    result.UserNotificationErrors++;
                    Logger.LogWarning(
                        exception,
                        "Retention cleanup failed for user notification {UserNotificationId}.",
                        candidate.Id);
                }
            }
        }

        await SaveCleanupCursorAsync(
            NotificationRetentionRecordKind.UserNotification,
            request,
            now,
            afterCreationTime,
            afterId,
            result.ScannedUserNotifications > scannedBefore || shouldResetCursor,
            cancellationToken);
    }

    protected virtual async Task CleanupDeliveriesAsync(
        NotificationRetentionCleanupRequest request,
        NotificationRetentionCleanupResult result,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!Options.Value.TerminalDeliveryRetention.HasValue)
        {
            return;
        }

        var cutoff = now - Options.Value.TerminalDeliveryRetention.Value;
        var cursor = await GetCleanupCursorAsync(
            NotificationRetentionRecordKind.NotificationDelivery,
            request,
            cancellationToken);
        var afterCreationTime = cursor?.LastCreationTime;
        var afterId = cursor?.LastRecordId;
        var hasWrapped = false;
        var shouldResetCursor = false;
        var scannedBefore = result.ScannedDeliveries;

        while (result.ScannedDeliveries < batchSize)
        {
            var remainingBatchSize = (int)(batchSize - result.ScannedDeliveries);
            var candidates = await GetDeliveryCandidatesAsync(
                request,
                cutoff,
                remainingBatchSize,
                afterCreationTime,
                afterId,
                cancellationToken);
            if (candidates.Count == 0)
            {
                if (!hasWrapped &&
                    afterCreationTime.HasValue &&
                    afterId.HasValue &&
                    result.ScannedDeliveries == scannedBefore)
                {
                    afterCreationTime = null;
                    afterId = null;
                    hasWrapped = true;
                    shouldResetCursor = true;
                    continue;
                }

                break;
            }

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.ScannedDeliveries++;
                afterCreationTime = candidate.CreationTime;
                afterId = candidate.Id;

                try
                {
                    await ProcessDeliveryCandidateAsync(
                        candidate,
                        cutoff,
                        request.IsDryRun,
                        result,
                        cancellationToken);
                }
                catch (Exception exception) when (IsRecoverableCleanupException(exception, cancellationToken))
                {
                    result.DeliveryErrors++;
                    Logger.LogWarning(
                        exception,
                        "Retention cleanup failed for notification delivery {DeliveryId}.",
                        candidate.Id);
                }
            }
        }

        await SaveCleanupCursorAsync(
            NotificationRetentionRecordKind.NotificationDelivery,
            request,
            now,
            afterCreationTime,
            afterId,
            result.ScannedDeliveries > scannedBefore || shouldResetCursor,
            cancellationToken);
    }

    protected virtual async Task CleanupNotificationsAsync(
        NotificationRetentionCleanupRequest request,
        NotificationRetentionCleanupResult result,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!Options.Value.OrphanNotificationRetention.HasValue)
        {
            return;
        }

        var cutoff = now - Options.Value.OrphanNotificationRetention.Value;
        var cursor = await GetCleanupCursorAsync(
            NotificationRetentionRecordKind.Notification,
            request,
            cancellationToken);
        var afterCreationTime = cursor?.LastCreationTime;
        var afterId = cursor?.LastRecordId;
        var hasWrapped = false;
        var shouldResetCursor = false;
        var scannedBefore = result.ScannedNotifications;

        while (result.ScannedNotifications < batchSize)
        {
            var remainingBatchSize = (int)(batchSize - result.ScannedNotifications);
            var candidates = await GetNotificationCandidatesAsync(
                request,
                cutoff,
                remainingBatchSize,
                afterCreationTime,
                afterId,
                cancellationToken);
            if (candidates.Count == 0)
            {
                if (!hasWrapped &&
                    afterCreationTime.HasValue &&
                    afterId.HasValue &&
                    result.ScannedNotifications == scannedBefore)
                {
                    afterCreationTime = null;
                    afterId = null;
                    hasWrapped = true;
                    shouldResetCursor = true;
                    continue;
                }

                break;
            }

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.ScannedNotifications++;
                afterCreationTime = candidate.CreationTime;
                afterId = candidate.Id;

                try
                {
                    await ProcessNotificationCandidateAsync(
                        candidate,
                        cutoff,
                        now,
                        request.IsDryRun,
                        result,
                        cancellationToken);
                }
                catch (Exception exception) when (IsRecoverableCleanupException(exception, cancellationToken))
                {
                    result.NotificationErrors++;
                    Logger.LogWarning(
                        exception,
                        "Retention cleanup failed for notification {NotificationId}.",
                        candidate.Id);
                }
            }
        }

        await SaveCleanupCursorAsync(
            NotificationRetentionRecordKind.Notification,
            request,
            now,
            afterCreationTime,
            afterId,
            result.ScannedNotifications > scannedBefore || shouldResetCursor,
            cancellationToken);
    }

    protected virtual async Task<List<UserNotification>> GetUserNotificationCandidatesAsync(
        NotificationRetentionCleanupRequest request,
        DateTime cutoff,
        int batchSize,
        DateTime? afterCreationTime,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = await UserNotificationRepository.GetQueryableAsync();
            query = ApplyTenantScope(query, request)
                .Where(userNotification =>
                    userNotification.State == UserNotificationState.Read &&
                    userNotification.CreationTime <= cutoff);
            query = ApplyCursor(query, afterCreationTime, afterId);

            var result = await AsyncExecuter.ToListAsync(query
                .OrderBy(userNotification => userNotification.CreationTime)
                .ThenBy(userNotification => userNotification.Id)
                .Take(batchSize), cancellationToken);

            await unitOfWork.CompleteAsync(cancellationToken);
            return result;
        }
    }

    protected virtual async Task<List<NotificationDeliveryRecord>> GetDeliveryCandidatesAsync(
        NotificationRetentionCleanupRequest request,
        DateTime cutoff,
        int batchSize,
        DateTime? afterCreationTime,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = await DeliveryRepository.GetQueryableAsync();
            query = ApplyTenantScope(query, request)
                .Where(delivery =>
                    (delivery.State == NotificationDeliveryState.Succeeded ||
                     delivery.State == NotificationDeliveryState.Suppressed ||
                     delivery.State == NotificationDeliveryState.DeadLettered) &&
                    ((delivery.CompletedTime.HasValue && delivery.CompletedTime <= cutoff) ||
                     (!delivery.CompletedTime.HasValue && delivery.CreationTime <= cutoff)));
            query = ApplyCursor(query, afterCreationTime, afterId);

            var result = await AsyncExecuter.ToListAsync(query
                .OrderBy(delivery => delivery.CreationTime)
                .ThenBy(delivery => delivery.Id)
                .Take(batchSize), cancellationToken);

            await unitOfWork.CompleteAsync(cancellationToken);
            return result;
        }
    }

    protected virtual async Task<List<Notification>> GetNotificationCandidatesAsync(
        NotificationRetentionCleanupRequest request,
        DateTime cutoff,
        int batchSize,
        DateTime? afterCreationTime,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = await NotificationRepository.GetQueryableAsync();
            query = ApplyTenantScope(query, request)
                .Where(notification => notification.CreationTime <= cutoff);
            query = ApplyCursor(query, afterCreationTime, afterId);

            var result = await AsyncExecuter.ToListAsync(query
                .OrderBy(notification => notification.CreationTime)
                .ThenBy(notification => notification.Id)
                .Take(batchSize), cancellationToken);

            await unitOfWork.CompleteAsync(cancellationToken);
            return result;
        }
    }

    protected virtual async Task ProcessUserNotificationCandidateAsync(
        UserNotification candidate,
        DateTime cutoff,
        bool isDryRun,
        NotificationRetentionCleanupResult result,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var entity = await UserNotificationRepository.FindAsync(
                candidate.Id,
                cancellationToken: cancellationToken);
            if (entity == null ||
                entity.State != UserNotificationState.Read ||
                entity.CreationTime > cutoff)
            {
                result.SkippedUserNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            var deletionCandidate = new NotificationRetentionCandidate(
                NotificationRetentionRecordKind.UserNotification,
                entity.Id,
                entity.TenantId,
                entity.CreationTime,
                "read-inbox-retention-expired",
                entity.NotificationId,
                entity.UserId);

            if (await IsVetoedAsync(deletionCandidate, result, cancellationToken))
            {
                result.SkippedUserNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            if (isDryRun)
            {
                result.DeletedUserNotifications++;
            }
            else
            {
                await UserNotificationRepository.DeleteAsync(entity, autoSave: true, cancellationToken: cancellationToken);
                result.DeletedUserNotifications++;
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    protected virtual async Task ProcessDeliveryCandidateAsync(
        NotificationDeliveryRecord candidate,
        DateTime cutoff,
        bool isDryRun,
        NotificationRetentionCleanupResult result,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var entity = await DeliveryRepository.FindAsync(candidate.Id, cancellationToken: cancellationToken);
            if (entity == null || !IsDeliveryRetentionEligible(entity, cutoff))
            {
                result.SkippedDeliveries++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            var deletionCandidate = new NotificationRetentionCandidate(
                NotificationRetentionRecordKind.NotificationDelivery,
                entity.Id,
                entity.TenantId,
                entity.CreationTime,
                "terminal-delivery-retention-expired",
                entity.NotificationId,
                entity.UserId,
                entity.Channel,
                entity.State);

            if (await IsVetoedAsync(deletionCandidate, result, cancellationToken))
            {
                result.SkippedDeliveries++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            if (isDryRun)
            {
                result.DeletedDeliveries++;
            }
            else
            {
                await DeliveryRepository.DeleteAsync(entity, autoSave: true, cancellationToken: cancellationToken);
                result.DeletedDeliveries++;
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    protected virtual async Task ProcessNotificationCandidateAsync(
        Notification candidate,
        DateTime cutoff,
        DateTime now,
        bool isDryRun,
        NotificationRetentionCleanupResult result,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var entity = await NotificationRepository.FindAsync(candidate.Id, cancellationToken: cancellationToken);
            if (entity == null || entity.CreationTime > cutoff)
            {
                result.SkippedNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            if (await HasNotificationReferencesAsync(entity.Id, entity.TenantId, cancellationToken))
            {
                await CancelRetentionDeletionIfPossibleAsync(entity, cancellationToken);
                result.SkippedNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            if (!entity.RetentionDeletionTime.HasValue)
            {
                if (isDryRun)
                {
                    result.DeletedNotifications++;
                }
                else
                {
                    entity.MarkRetentionDeletion(now);
                    await NotificationRepository.UpdateAsync(entity, autoSave: true, cancellationToken: cancellationToken);
                    result.SkippedNotifications++;
                }

                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            if (entity.RetentionDeletionTime.Value > now - Options.Value.NotificationDeletionQuarantineDuration)
            {
                result.SkippedNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            var deletionCandidate = new NotificationRetentionCandidate(
                NotificationRetentionRecordKind.Notification,
                entity.Id,
                entity.TenantId,
                entity.CreationTime,
                "orphan-notification-retention-expired",
                entity.Id);

            if (await IsVetoedAsync(deletionCandidate, result, cancellationToken))
            {
                result.SkippedNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            // Re-check immediately before deletion so an archive hook or concurrent delivery that materializes a
            // retained inbox/delivery row cannot race a payload delete in the same cleanup pass.
            if (await HasNotificationReferencesAsync(entity.Id, entity.TenantId, cancellationToken))
            {
                await CancelRetentionDeletionIfPossibleAsync(entity, cancellationToken);
                result.SkippedNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            await BeforeDeleteNotificationAsync(entity, cancellationToken);

            if (isDryRun)
            {
                result.DeletedNotifications++;
            }
            else
            {
                await NotificationRepository.DeleteAsync(entity, autoSave: true, cancellationToken: cancellationToken);
                result.DeletedNotifications++;
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    protected virtual Task BeforeDeleteNotificationAsync(
        Notification notification,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual async Task CancelRetentionDeletionIfPossibleAsync(
        Notification notification,
        CancellationToken cancellationToken)
    {
        if (!notification.RetentionDeletionTime.HasValue)
        {
            return;
        }

        try
        {
            notification.CancelRetentionDeletion();
            await NotificationRepository.UpdateAsync(
                notification,
                autoSave: true,
                cancellationToken: cancellationToken);
        }
        catch (AbpDbConcurrencyException)
        {
            // Another retained-reference writer already cleared or changed the marker. The important invariant is
            // that a same-tenant retained row exists, so payload deletion must be skipped either way.
        }
    }

    protected virtual async Task<bool> IsVetoedAsync(
        NotificationRetentionCandidate candidate,
        NotificationRetentionCleanupResult result,
        CancellationToken cancellationToken)
    {
        foreach (var contributor in DeletionContributors)
        {
            var decision = await contributor.EvaluateAsync(candidate, cancellationToken);
            if (decision.IsVetoed)
            {
                SetOldestRetainedTimestamp(result, candidate.RecordKind, candidate.CreationTime);
                Logger.LogInformation(
                    "Retention cleanup retained {RecordKind} {RecordId}: {Reason}",
                    candidate.RecordKind,
                    candidate.Id,
                    decision.Reason);
                return true;
            }
        }

        return false;
    }

    protected virtual async Task<bool> HasNotificationReferencesAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var userNotificationQuery = await UserNotificationRepository.GetQueryableAsync();
        var hasUserNotification = await AsyncExecuter.AnyAsync(userNotificationQuery.Where(userNotification =>
            userNotification.NotificationId == notificationId &&
            userNotification.TenantId == tenantId), cancellationToken);
        if (hasUserNotification)
        {
            return true;
        }

        var deliveryQuery = await DeliveryRepository.GetQueryableAsync();
        var tenantKey = tenantId ?? Guid.Empty;
        return await AsyncExecuter.AnyAsync(deliveryQuery.Where(delivery =>
            delivery.NotificationId == notificationId &&
            delivery.TenantKey == tenantKey), cancellationToken);
    }

    protected virtual async Task<NotificationRetentionCleanupCursor?> GetCleanupCursorAsync(
        NotificationRetentionRecordKind recordKind,
        NotificationRetentionCleanupRequest request,
        CancellationToken cancellationToken)
    {
        if (request.IsDryRun)
        {
            return null;
        }

        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var cursor = await CleanupCursorRepository.FindAsync(
                CreateCleanupCursorId(recordKind, request),
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return cursor;
        }
    }

    protected virtual async Task SaveCleanupCursorAsync(
        NotificationRetentionRecordKind recordKind,
        NotificationRetentionCleanupRequest request,
        DateTime now,
        DateTime? afterCreationTime,
        Guid? afterId,
        bool shouldSave,
        CancellationToken cancellationToken)
    {
        if (request.IsDryRun || !shouldSave)
        {
            return;
        }

        try
        {
            using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            using (DataFilter.Disable<IMultiTenant>())
            {
                var cursorId = CreateCleanupCursorId(recordKind, request);
                var cursor = await CleanupCursorRepository.FindAsync(cursorId, cancellationToken: cancellationToken);
                if (cursor == null)
                {
                    if (!afterCreationTime.HasValue || !afterId.HasValue)
                    {
                        await unitOfWork.CompleteAsync(cancellationToken);
                        return;
                    }

                    cursor = new NotificationRetentionCleanupCursor(
                        cursorId,
                        recordKind,
                        request.IsTenantScoped,
                        request.TenantId,
                        now);
                    cursor.MoveTo(afterCreationTime.Value, afterId.Value, now);
                    await CleanupCursorRepository.InsertAsync(cursor, autoSave: true, cancellationToken: cancellationToken);
                }
                else if (afterCreationTime.HasValue && afterId.HasValue)
                {
                    cursor.MoveTo(afterCreationTime.Value, afterId.Value, now);
                    await CleanupCursorRepository.UpdateAsync(cursor, autoSave: true, cancellationToken: cancellationToken);
                }
                else
                {
                    cursor.Reset(now);
                    await CleanupCursorRepository.UpdateAsync(cursor, autoSave: true, cancellationToken: cancellationToken);
                }

                await unitOfWork.CompleteAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (IsRecoverableCleanupException(exception, cancellationToken))
        {
            Logger.LogWarning(
                exception,
                "Retention cleanup failed to persist the {RecordKind} scan cursor.",
                recordKind);
        }
    }

    protected virtual async Task SetOldestRetainedTimestampsAsync(
        NotificationRetentionCleanupRequest request,
        NotificationRetentionCleanupResult result,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            result.OldestRetainedNotificationCreationTime ??= await GetOldestNotificationCreationTimeAsync(
                request,
                cancellationToken);
            result.OldestRetainedUserNotificationCreationTime ??= await GetOldestUserNotificationCreationTimeAsync(
                request,
                cancellationToken);
            result.OldestRetainedDeliveryCreationTime ??= await GetOldestDeliveryCreationTimeAsync(
                request,
                cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    protected virtual async Task<DateTime?> GetOldestNotificationCreationTimeAsync(
        NotificationRetentionCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var query = await NotificationRepository.GetQueryableAsync();
        var page = await AsyncExecuter.ToListAsync(ApplyTenantScope(query, request)
            .OrderBy(notification => notification.CreationTime)
            .Select(notification => (DateTime?)notification.CreationTime)
            .Take(1), cancellationToken);
        return page.FirstOrDefault();
    }

    protected virtual async Task<DateTime?> GetOldestUserNotificationCreationTimeAsync(
        NotificationRetentionCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var query = await UserNotificationRepository.GetQueryableAsync();
        var page = await AsyncExecuter.ToListAsync(ApplyTenantScope(query, request)
            .OrderBy(userNotification => userNotification.CreationTime)
            .Select(userNotification => (DateTime?)userNotification.CreationTime)
            .Take(1), cancellationToken);
        return page.FirstOrDefault();
    }

    protected virtual async Task<DateTime?> GetOldestDeliveryCreationTimeAsync(
        NotificationRetentionCleanupRequest request,
        CancellationToken cancellationToken)
    {
        var query = await DeliveryRepository.GetQueryableAsync();
        var page = await AsyncExecuter.ToListAsync(ApplyTenantScope(query, request)
            .OrderBy(delivery => delivery.CreationTime)
            .Select(delivery => (DateTime?)delivery.CreationTime)
            .Take(1), cancellationToken);
        return page.FirstOrDefault();
    }

    protected virtual IQueryable<T> ApplyTenantScope<T>(
        IQueryable<T> query,
        NotificationRetentionCleanupRequest request)
        where T : IMultiTenant
    {
        if (!request.IsTenantScoped)
        {
            return query;
        }

        return query.Where(entity => entity.TenantId == request.TenantId);
    }

    private static IQueryable<Notification> ApplyCursor(
        IQueryable<Notification> query,
        DateTime? afterCreationTime,
        Guid? afterId)
    {
        if (!afterCreationTime.HasValue || !afterId.HasValue)
        {
            return query;
        }

        var creationTime = afterCreationTime.Value;
        var id = afterId.Value;
        return query.Where(notification =>
            notification.CreationTime > creationTime ||
            notification.CreationTime == creationTime && notification.Id.CompareTo(id) > 0);
    }

    private static IQueryable<UserNotification> ApplyCursor(
        IQueryable<UserNotification> query,
        DateTime? afterCreationTime,
        Guid? afterId)
    {
        if (!afterCreationTime.HasValue || !afterId.HasValue)
        {
            return query;
        }

        var creationTime = afterCreationTime.Value;
        var id = afterId.Value;
        return query.Where(userNotification =>
            userNotification.CreationTime > creationTime ||
            userNotification.CreationTime == creationTime && userNotification.Id.CompareTo(id) > 0);
    }

    private static IQueryable<NotificationDeliveryRecord> ApplyCursor(
        IQueryable<NotificationDeliveryRecord> query,
        DateTime? afterCreationTime,
        Guid? afterId)
    {
        if (!afterCreationTime.HasValue || !afterId.HasValue)
        {
            return query;
        }

        var creationTime = afterCreationTime.Value;
        var id = afterId.Value;
        return query.Where(delivery =>
            delivery.CreationTime > creationTime ||
            delivery.CreationTime == creationTime && delivery.Id.CompareTo(id) > 0);
    }

    private static bool IsDeliveryRetentionEligible(NotificationDeliveryRecord delivery, DateTime cutoff)
    {
        return IsTerminal(delivery.State) &&
               ((delivery.CompletedTime.HasValue && delivery.CompletedTime <= cutoff) ||
                (!delivery.CompletedTime.HasValue && delivery.CreationTime <= cutoff));
    }

    private static bool IsTerminal(NotificationDeliveryState state)
    {
        return state == NotificationDeliveryState.Succeeded ||
               state == NotificationDeliveryState.Suppressed ||
               state == NotificationDeliveryState.DeadLettered;
    }

    private static void SetOldestRetainedTimestamp(
        NotificationRetentionCleanupResult result,
        NotificationRetentionRecordKind recordKind,
        DateTime creationTime)
    {
        switch (recordKind)
        {
            case NotificationRetentionRecordKind.Notification:
                result.OldestRetainedNotificationCreationTime = Min(
                    result.OldestRetainedNotificationCreationTime,
                    creationTime);
                break;
            case NotificationRetentionRecordKind.UserNotification:
                result.OldestRetainedUserNotificationCreationTime = Min(
                    result.OldestRetainedUserNotificationCreationTime,
                    creationTime);
                break;
            case NotificationRetentionRecordKind.NotificationDelivery:
                result.OldestRetainedDeliveryCreationTime = Min(
                    result.OldestRetainedDeliveryCreationTime,
                    creationTime);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(recordKind), recordKind, null);
        }
    }

    private static DateTime Min(DateTime? current, DateTime candidate)
    {
        return !current.HasValue || candidate < current.Value ? candidate : current.Value;
    }

    private static Guid CreateCleanupCursorId(
        NotificationRetentionRecordKind recordKind,
        NotificationRetentionCleanupRequest request)
    {
        var tenantKey = request.IsTenantScoped
            ? (request.TenantId ?? Guid.Empty).ToString("N", CultureInfo.InvariantCulture)
            : "all-tenants";
        var hash = ComputeHash(
            "notification-retention-cleanup-cursor",
            recordKind.ToString(),
            request.IsTenantScoped ? "tenant-scoped" : "global",
            tenantKey);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private static byte[] ComputeHash(params string[] parts)
    {
        var canonical = new StringBuilder();
        foreach (var part in parts)
        {
            canonical.Append(part.Length.ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(part);
        }

        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private static void RecordMetrics(NotificationRetentionCleanupResult result)
    {
        NotificationRetentionMetrics.Record(
            "notification",
            result.IsDryRun,
            result.ScannedNotifications,
            result.DeletedNotifications,
            result.SkippedNotifications,
            result.NotificationErrors);
        NotificationRetentionMetrics.Record(
            "user_notification",
            result.IsDryRun,
            result.ScannedUserNotifications,
            result.DeletedUserNotifications,
            result.SkippedUserNotifications,
            result.UserNotificationErrors);
        NotificationRetentionMetrics.Record(
            "notification_delivery",
            result.IsDryRun,
            result.ScannedDeliveries,
            result.DeletedDeliveries,
            result.SkippedDeliveries,
            result.DeliveryErrors);
        NotificationRetentionMetrics.Record(
            "audience_broadcast_state",
            result.IsDryRun,
            result.ScannedAudienceBroadcastStates,
            result.DeletedAudienceBroadcastStates,
            result.SkippedAudienceBroadcastStates,
            result.AudienceBroadcastStateErrors);
        NotificationRetentionMetrics.RecordOldestRetained(
            result.OldestRetainedNotificationCreationTime,
            result.OldestRetainedUserNotificationCreationTime,
            result.OldestRetainedDeliveryCreationTime);
    }

    private static void ValidateBatchSize(int batchSize)
    {
        if (batchSize < 1 || batchSize > NotificationRetentionOptions.MaxCleanupBatchSize)
        {
            throw new InvalidOperationException(
                $"{nameof(NotificationRetentionCleanupRequest.BatchSize)} must be between 1 and " +
                $"{NotificationRetentionOptions.MaxCleanupBatchSize}.");
        }
    }

    private static bool IsRecoverableCleanupException(Exception exception, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested &&
               exception is not OperationCanceledException &&
               exception is not OutOfMemoryException &&
               exception is not StackOverflowException &&
               exception is not AccessViolationException &&
               exception is not AppDomainUnloadedException &&
               exception is not BadImageFormatException &&
               exception is not CannotUnloadAppDomainException &&
               exception is not InvalidProgramException;
    }
}
