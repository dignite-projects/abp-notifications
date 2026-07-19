using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Linq;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Deletes aged read inbox rows and orphaned notification payloads in bounded batches. Cleanup is best-effort
/// and idempotent; a consuming host can invoke it directly or enable the periodic cleanup worker.
/// </summary>
public class NotificationRetentionManager : DomainService
{
    protected IRepository<Notification, Guid> NotificationRepository { get; }
    protected IRepository<UserNotification, Guid> UserNotificationRepository { get; }
    protected IAsyncQueryableExecuter AsyncExecuter { get; }
    protected IOptions<NotificationRetentionOptions> Options { get; }

    public NotificationRetentionManager(
        IRepository<Notification, Guid> notificationRepository,
        IRepository<UserNotification, Guid> userNotificationRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IOptions<NotificationRetentionOptions> options)
    {
        NotificationRepository = notificationRepository;
        UserNotificationRepository = userNotificationRepository;
        AsyncExecuter = asyncExecuter;
        Options = options;
    }

    public virtual async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        var now = Clock.Now;
        var batchSize = Options.Value.CleanupBatchSize;
        await CleanupReadUserNotificationsAsync(now, batchSize, cancellationToken);
        await CleanupOrphanNotificationsAsync(now, batchSize, cancellationToken);
    }

    protected virtual async Task CleanupReadUserNotificationsAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var retention = Options.Value.ReadUserNotificationRetention;
        if (!retention.HasValue)
        {
            return;
        }

        var cutoff = now - retention.Value;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = await UserNotificationRepository.GetQueryableAsync();
            var batch = await AsyncExecuter.ToListAsync(
                query.Where(row => row.State == UserNotificationState.Read && row.CreationTime < cutoff)
                    .OrderBy(row => row.CreationTime)
                    .Take(batchSize),
                cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            await UserNotificationRepository.DeleteManyAsync(
                batch,
                autoSave: true,
                cancellationToken: cancellationToken);
            if (batch.Count < batchSize)
            {
                break;
            }
        }
    }

    protected virtual async Task CleanupOrphanNotificationsAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var retention = Options.Value.OrphanNotificationRetention;
        if (!retention.HasValue)
        {
            return;
        }

        var cutoff = now - retention.Value;
        // Keyset on CreationTime so still-referenced payloads do not stall the scan. Cross-collection joins are not
        // portable to MongoDB, so the referencing inbox rows are resolved with a bounded second query per batch.
        DateTime? afterCreationTime = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var notificationQuery = await NotificationRepository.GetQueryableAsync();
            var scoped = notificationQuery.Where(notification => notification.CreationTime < cutoff);
            if (afterCreationTime.HasValue)
            {
                scoped = scoped.Where(notification => notification.CreationTime > afterCreationTime.Value);
            }

            var candidates = await AsyncExecuter.ToListAsync(
                scoped.OrderBy(notification => notification.CreationTime).Take(batchSize),
                cancellationToken);
            if (candidates.Count == 0)
            {
                break;
            }

            afterCreationTime = candidates[^1].CreationTime;
            var candidateIds = candidates.Select(notification => notification.Id).ToList();
            var userNotificationQuery = await UserNotificationRepository.GetQueryableAsync();
            var referencedIds = (await AsyncExecuter.ToListAsync(
                    userNotificationQuery
                        .Where(row => candidateIds.Contains(row.NotificationId))
                        .Select(row => row.NotificationId)
                        .Distinct(),
                    cancellationToken))
                .ToHashSet();

            var orphans = candidates.Where(notification => !referencedIds.Contains(notification.Id)).ToList();
            if (orphans.Count > 0)
            {
                await NotificationRepository.DeleteManyAsync(
                    orphans,
                    autoSave: true,
                    cancellationToken: cancellationToken);
            }

            if (candidates.Count < batchSize)
            {
                break;
            }
        }
    }
}
