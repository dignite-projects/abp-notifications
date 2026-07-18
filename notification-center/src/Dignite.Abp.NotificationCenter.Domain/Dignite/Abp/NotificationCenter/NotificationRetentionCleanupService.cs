using System;
using System.Collections.Generic;
using System.Linq;
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
    protected IAsyncQueryableExecuter AsyncExecuter { get; }
    protected IDataFilter DataFilter { get; }
    protected IClock Clock { get; }
    protected IUnitOfWorkManager UnitOfWorkManager { get; }
    protected IOptions<NotificationRetentionOptions> Options { get; }
    protected IReadOnlyList<INotificationRetentionDeletionContributor> DeletionContributors { get; }
    protected ILogger<NotificationRetentionCleanupService> Logger { get; }

    public NotificationRetentionCleanupService(
        IRepository<Notification, Guid> notificationRepository,
        IRepository<UserNotification, Guid> userNotificationRepository,
        IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<NotificationRetentionOptions> options,
        IEnumerable<INotificationRetentionDeletionContributor> deletionContributors,
        ILogger<NotificationRetentionCleanupService> logger)
    {
        NotificationRepository = notificationRepository;
        UserNotificationRepository = userNotificationRepository;
        DeliveryRepository = deliveryRepository;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
        Clock = clock;
        UnitOfWorkManager = unitOfWorkManager;
        Options = options;
        DeletionContributors = deletionContributors.ToArray();
        Logger = logger;
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
        var candidates = await GetUserNotificationCandidatesAsync(request, cutoff, batchSize, cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ScannedUserNotifications++;

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
        var candidates = await GetDeliveryCandidatesAsync(request, cutoff, batchSize, cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ScannedDeliveries++;

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
        var candidates = await GetNotificationCandidatesAsync(request, cutoff, batchSize, cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ScannedNotifications++;

            try
            {
                await ProcessNotificationCandidateAsync(
                    candidate,
                    cutoff,
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

    protected virtual async Task<List<UserNotification>> GetUserNotificationCandidatesAsync(
        NotificationRetentionCleanupRequest request,
        DateTime cutoff,
        int batchSize,
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
                     delivery.State == NotificationDeliveryState.DeadLetter) &&
                    ((delivery.CompletedTime.HasValue && delivery.CompletedTime <= cutoff) ||
                     (!delivery.CompletedTime.HasValue && delivery.CreationTime <= cutoff)));

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
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = await NotificationRepository.GetQueryableAsync();
            query = ApplyTenantScope(query, request)
                .Where(notification => notification.CreationTime <= cutoff);

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

            result.DeletedUserNotifications++;
            if (!isDryRun)
            {
                await UserNotificationRepository.DeleteAsync(
                    entity,
                    autoSave: true,
                    cancellationToken: cancellationToken);
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

            result.DeletedDeliveries++;
            if (!isDryRun)
            {
                await DeliveryRepository.DeleteAsync(entity, autoSave: true, cancellationToken: cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    protected virtual async Task ProcessNotificationCandidateAsync(
        Notification candidate,
        DateTime cutoff,
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
                result.SkippedNotifications++;
                await unitOfWork.CompleteAsync(cancellationToken);
                return;
            }

            result.DeletedNotifications++;
            if (!isDryRun)
            {
                await NotificationRepository.DeleteAsync(entity, autoSave: true, cancellationToken: cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
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
               state == NotificationDeliveryState.DeadLetter;
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
