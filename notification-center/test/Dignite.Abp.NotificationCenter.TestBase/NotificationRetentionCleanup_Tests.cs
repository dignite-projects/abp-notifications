using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Provider-agnostic retention cleanup scenarios shared by EF Core and MongoDB.</summary>
public abstract class NotificationRetentionCleanup_Tests<TStartupModule> : NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    public NotificationRetentionCleanup_Tests()
    {
        TestNotificationRetentionDeletionContributor.Reset();
    }

    [Fact]
    public async Task Expired_orphans_are_deleted_but_unread_inbox_and_active_delivery_records_protect_payloads()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-120);
        var orphanId = Guid.NewGuid();
        var readNotificationId = Guid.NewGuid();
        var readUserNotificationId = Guid.NewGuid();
        var unreadNotificationId = Guid.NewGuid();
        var unreadUserNotificationId = Guid.NewGuid();
        var pendingNotificationId = Guid.NewGuid();
        var pendingUserId = Guid.NewGuid();
        var pendingDeliveryId = NewDeliveryId(pendingNotificationId, pendingUserId, "Email", null);
        var terminalNotificationId = Guid.NewGuid();
        var terminalUserId = Guid.NewGuid();
        var terminalDeliveryId = NewDeliveryId(terminalNotificationId, terminalUserId, "SignalR", null);

        await InsertNotificationAsync(orphanId, old);
        await InsertNotificationAsync(readNotificationId, old);
        await InsertUserNotificationAsync(
            readUserNotificationId,
            readNotificationId,
            Guid.NewGuid(),
            UserNotificationState.Read,
            old);
        await InsertNotificationAsync(unreadNotificationId, old);
        await InsertUserNotificationAsync(
            unreadUserNotificationId,
            unreadNotificationId,
            Guid.NewGuid(),
            UserNotificationState.Unread,
            old);
        await InsertNotificationAsync(pendingNotificationId, old);
        await InsertDeliveryAsync(pendingDeliveryId, pendingNotificationId, pendingUserId, "Email", old);
        await InsertNotificationAsync(terminalNotificationId, old);
        await InsertSucceededDeliveryAsync(
            terminalDeliveryId,
            terminalNotificationId,
            terminalUserId,
            "SignalR",
            old,
            old.AddMinutes(1));

        var firstPass = await CleanupAsync(now);

        firstPass.DeletedUserNotifications.ShouldBe(1);
        firstPass.DeletedDeliveries.ShouldBe(1);
        firstPass.DeletedNotifications.ShouldBe(0);
        firstPass.SkippedNotifications.ShouldBe(5);

        (await ExistsAsync<Notification>(orphanId)).ShouldBeTrue();
        (await ExistsAsync<UserNotification>(readUserNotificationId)).ShouldBeFalse();
        (await ExistsAsync<NotificationDeliveryRecord>(terminalDeliveryId)).ShouldBeFalse();

        var secondPass = await CleanupAsync(now.AddMinutes(6));

        secondPass.DeletedNotifications.ShouldBe(3);
        secondPass.SkippedNotifications.ShouldBe(2);

        (await ExistsAsync<Notification>(orphanId)).ShouldBeFalse();
        (await ExistsAsync<Notification>(readNotificationId)).ShouldBeFalse();
        (await ExistsAsync<UserNotification>(unreadUserNotificationId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(unreadNotificationId)).ShouldBeTrue();
        (await ExistsAsync<NotificationDeliveryRecord>(pendingDeliveryId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(pendingNotificationId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(terminalNotificationId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Dry_run_reports_eligible_deletions_without_removing_records()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var notificationId = Guid.NewGuid();
        await InsertNotificationAsync(notificationId, now.AddDays(-120));

        var result = await CleanupAsync(now, isDryRun: true);

        result.IsDryRun.ShouldBeTrue();
        result.DeletedNotifications.ShouldBe(1);
        result.DeletedCount.ShouldBe(1);
        (await ExistsAsync<Notification>(notificationId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Archive_or_veto_contributors_can_retain_candidates_and_failures_are_recoverable()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var retainedId = Guid.NewGuid();
        var failingId = Guid.NewGuid();
        var old = now.AddDays(-120);
        await InsertNotificationAsync(retainedId, old, retentionDeletionTime: old.AddMinutes(2));
        await InsertNotificationAsync(failingId, old.AddMinutes(1), retentionDeletionTime: old.AddMinutes(3));
        TestNotificationRetentionDeletionContributor.VetoReasons[retainedId] = "audit-hold";
        TestNotificationRetentionDeletionContributor.ThrowReasons[failingId] = "archive-writer-unavailable";

        var result = await CleanupAsync(now);

        result.ScannedNotifications.ShouldBe(2);
        result.DeletedNotifications.ShouldBe(0);
        result.SkippedNotifications.ShouldBe(1);
        result.NotificationErrors.ShouldBe(1);
        result.OldestRetainedNotificationCreationTime.ShouldNotBeNull();
        (await ExistsAsync<Notification>(retainedId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(failingId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Cleanup_respects_batch_budget_per_record_kind_and_restarts()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-120);
        var firstNotificationId = Guid.NewGuid();
        var secondNotificationId = Guid.NewGuid();
        var thirdNotificationId = Guid.NewGuid();
        var firstUserNotificationId = Guid.NewGuid();
        var secondUserNotificationId = Guid.NewGuid();
        var thirdUserNotificationId = Guid.NewGuid();

        await InsertNotificationAsync(firstNotificationId, old);
        await InsertNotificationAsync(secondNotificationId, old.AddMinutes(1));
        await InsertNotificationAsync(thirdNotificationId, old.AddMinutes(2));
        await InsertUserNotificationAsync(
            firstUserNotificationId,
            firstNotificationId,
            Guid.NewGuid(),
            UserNotificationState.Read,
            old);
        await InsertUserNotificationAsync(
            secondUserNotificationId,
            secondNotificationId,
            Guid.NewGuid(),
            UserNotificationState.Read,
            old.AddMinutes(1));
        await InsertUserNotificationAsync(
            thirdUserNotificationId,
            thirdNotificationId,
            Guid.NewGuid(),
            UserNotificationState.Read,
            old.AddMinutes(2));

        var firstPass = await CleanupAsync(now, batchSize: 2);

        firstPass.ScannedUserNotifications.ShouldBe(2);
        firstPass.DeletedUserNotifications.ShouldBe(2);
        (await ExistsAsync<UserNotification>(firstUserNotificationId)).ShouldBeFalse();
        (await ExistsAsync<UserNotification>(secondUserNotificationId)).ShouldBeFalse();
        (await ExistsAsync<UserNotification>(thirdUserNotificationId)).ShouldBeTrue();

        var secondPass = await CleanupAsync(now, batchSize: 2);

        secondPass.ScannedUserNotifications.ShouldBe(1);
        secondPass.DeletedUserNotifications.ShouldBe(1);
        (await ExistsAsync<UserNotification>(thirdUserNotificationId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_reference_created_between_archive_and_delete_protects_the_base_notification()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-120);
        var notificationId = Guid.NewGuid();
        var userNotificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await InsertNotificationAsync(notificationId, old, retentionDeletionTime: old.AddMinutes(1));
        TestNotificationRetentionDeletionContributor.Callbacks[notificationId] = async (candidate, cancellationToken) =>
        {
            await InsertUserNotificationThroughStoreAsync(
                userNotificationId,
                candidate.NotificationId!.Value,
                userId,
                UserNotificationState.Unread,
                old,
                candidate.TenantId,
                cancellationToken);
        };

        var result = await CleanupAsync(now);

        result.DeletedNotifications.ShouldBe(0);
        result.SkippedNotifications.ShouldBe(1);
        (await ExistsAsync<Notification>(notificationId)).ShouldBeTrue();
        (await ExistsAsync<UserNotification>(userNotificationId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Reference_created_after_the_final_check_causes_a_concurrency_failure_instead_of_payload_deletion()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-120);
        var notificationId = Guid.NewGuid();
        var userNotificationId = Guid.NewGuid();
        await InsertNotificationAsync(notificationId, old, retentionDeletionTime: old.AddMinutes(1));
        var service = new RaceAfterFinalCheckCleanupService(
            GetRequiredService<IRepository<Notification, Guid>>(),
            GetRequiredService<IRepository<UserNotification, Guid>>(),
            GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>(),
            GetRequiredService<IRepository<NotificationRetentionCleanupCursor, Guid>>(),
            GetRequiredService<IAsyncQueryableExecuter>(),
            GetRequiredService<IDataFilter>(),
            GetRequiredService<IClock>(),
            GetRequiredService<IUnitOfWorkManager>(),
            GetRequiredService<IOptions<NotificationRetentionOptions>>(),
            new[] { GetRequiredService<INotificationRetentionDeletionContributor>() },
            GetRequiredService<ILogger<NotificationRetentionCleanupService>>(),
            cancellationToken => InsertUserNotificationThroughStoreAsync(
                userNotificationId,
                notificationId,
                Guid.NewGuid(),
                UserNotificationState.Unread,
                old,
                cancellationToken: cancellationToken));

        var result = await service.CleanupAsync(new NotificationRetentionCleanupRequest
        {
            Now = now
        });

        result.DeletedNotifications.ShouldBe(0);
        result.NotificationErrors.ShouldBe(1);
        (await ExistsAsync<Notification>(notificationId)).ShouldBeTrue();
        (await ExistsAsync<UserNotification>(userNotificationId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Tenant_local_reference_checks_do_not_mix_host_or_other_tenant_records()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-120);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantANotificationId = Guid.NewGuid();
        var tenantBInboxRowId = Guid.NewGuid();
        await InsertNotificationAsync(tenantANotificationId, old, tenantA, old.AddMinutes(1));
        await InsertUserNotificationAsync(
            tenantBInboxRowId,
            tenantANotificationId,
            Guid.NewGuid(),
            UserNotificationState.Unread,
            old,
            tenantB);

        var result = await CleanupAsync(now);

        result.DeletedNotifications.ShouldBe(1);
        (await ExistsAsync<Notification>(tenantANotificationId)).ShouldBeFalse();
        (await ExistsAsync<UserNotification>(tenantBInboxRowId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Cursor_continues_after_budget_is_filled_by_skipped_candidates()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var firstProtectedId = Guid.NewGuid();
        var firstProtectedInboxId = Guid.NewGuid();
        var secondProtectedId = Guid.NewGuid();
        var secondProtectedInboxId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        await InsertNotificationAsync(firstProtectedId, now.AddDays(-122));
        await InsertUserNotificationAsync(
            firstProtectedInboxId,
            firstProtectedId,
            Guid.NewGuid(),
            UserNotificationState.Unread,
            now.AddDays(-122));
        await InsertNotificationAsync(secondProtectedId, now.AddDays(-121));
        await InsertUserNotificationAsync(
            secondProtectedInboxId,
            secondProtectedId,
            Guid.NewGuid(),
            UserNotificationState.Unread,
            now.AddDays(-121));
        await InsertNotificationAsync(orphanId, now.AddDays(-120));

        var firstPass = await CleanupAsync(now, batchSize: 2);

        firstPass.ScannedNotifications.ShouldBe(2);
        firstPass.DeletedNotifications.ShouldBe(0);
        firstPass.SkippedNotifications.ShouldBe(2);
        (await ExistsAsync<Notification>(orphanId)).ShouldBeTrue();

        var secondPass = await CleanupAsync(now, batchSize: 2);

        secondPass.ScannedNotifications.ShouldBe(1);
        secondPass.DeletedNotifications.ShouldBe(0);
        secondPass.SkippedNotifications.ShouldBe(1);
        (await ExistsAsync<Notification>(orphanId)).ShouldBeTrue();

        await CleanupAsync(now.AddMinutes(6), batchSize: 2);
        var fourthPass = await CleanupAsync(now.AddMinutes(6), batchSize: 2);

        fourthPass.DeletedNotifications.ShouldBe(1);
        (await ExistsAsync<Notification>(firstProtectedId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(secondProtectedId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(orphanId)).ShouldBeFalse();
    }

    private async Task<NotificationRetentionCleanupResult> CleanupAsync(
        DateTime now,
        bool isDryRun = false,
        int? batchSize = null)
    {
        return await GetRequiredService<INotificationRetentionCleanupService>().CleanupAsync(
            new NotificationRetentionCleanupRequest
            {
                Now = now,
                IsDryRun = isDryRun,
                BatchSize = batchSize
            });
    }

    private async Task InsertNotificationAsync(
        Guid notificationId,
        DateTime creationTime,
        Guid? tenantId = null,
        DateTime? retentionDeletionTime = null)
    {
        await WithTenantUnitOfWorkAsync(tenantId, async () =>
        {
            var notification = new Notification(
                notificationId,
                "order.shipped",
                data: null,
                entityTypeName: null,
                entityId: null,
                NotificationSeverity.Info,
                creationTime,
                tenantId);
            if (retentionDeletionTime.HasValue)
            {
                notification.MarkRetentionDeletion(retentionDeletionTime.Value);
            }

            await GetRequiredService<IRepository<Notification, Guid>>().InsertAsync(notification);
        });
    }

    private async Task InsertUserNotificationAsync(
        Guid id,
        Guid notificationId,
        Guid userId,
        UserNotificationState state,
        DateTime creationTime,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await WithTenantUnitOfWorkAsync(tenantId, async () =>
        {
            await GetRequiredService<IRepository<UserNotification, Guid>>().InsertAsync(new UserNotification(
                id,
                userId,
                notificationId,
                state,
                creationTime,
                tenantId), cancellationToken: cancellationToken);
        });
    }

    private async Task InsertUserNotificationThroughStoreAsync(
        Guid id,
        Guid notificationId,
        Guid userId,
        UserNotificationState state,
        DateTime creationTime,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await WithTenantUnitOfWorkAsync(tenantId, async () =>
        {
            await GetRequiredService<INotificationStore>().InsertUserNotificationAsync(new UserNotificationInfo
            {
                Id = id,
                NotificationId = notificationId,
                UserId = userId,
                State = state,
                CreationTime = creationTime,
                TenantId = tenantId
            });
        });
    }

    private async Task InsertDeliveryAsync(
        Guid id,
        Guid notificationId,
        Guid userId,
        string channel,
        DateTime creationTime,
        Guid? tenantId = null)
    {
        await WithTenantUnitOfWorkAsync(tenantId, async () =>
        {
            await GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>().InsertAsync(
                NewDeliveryRecord(id, notificationId, userId, channel, creationTime, tenantId));
        });
    }

    private async Task InsertSucceededDeliveryAsync(
        Guid id,
        Guid notificationId,
        Guid userId,
        string channel,
        DateTime creationTime,
        DateTime completedTime,
        Guid? tenantId = null)
    {
        var record = NewDeliveryRecord(id, notificationId, userId, channel, creationTime, tenantId);
        var claim = record.Claim(Guid.NewGuid(), completedTime.AddMinutes(-1), TimeSpan.FromMinutes(2));
        record.MarkSucceeded(claim.LeaseId, completedTime);

        await WithTenantUnitOfWorkAsync(tenantId, async () =>
        {
            await GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>().InsertAsync(record);
        });
    }

    private static NotificationDeliveryRecord NewDeliveryRecord(
        Guid id,
        Guid notificationId,
        Guid userId,
        string channel,
        DateTime creationTime,
        Guid? tenantId)
    {
        return new NotificationDeliveryRecord(
            id,
            notificationId,
            userId,
            channel,
            NotificationDeliveryIdentity.CreateIdempotencyKey(tenantId, notificationId, userId, channel),
            "order.shipped",
            data: null,
            entityTypeName: null,
            entityId: null,
            NotificationSeverity.Info,
            creationTime,
            tenantId);
    }

    private static Guid NewDeliveryId(Guid notificationId, Guid userId, string channel, Guid? tenantId)
    {
        return NotificationDeliveryIdentity.CreateId(tenantId, notificationId, userId, channel);
    }

    private async Task<bool> ExistsAsync<TEntity>(Guid id)
        where TEntity : class, IEntity<Guid>
    {
        bool exists = false;
        await WithUnitOfWorkAsync(async () =>
        {
            using (GetRequiredService<IDataFilter>().Disable<IMultiTenant>())
            {
                exists = (await GetRequiredService<IRepository<TEntity, Guid>>().FindAsync(id)) != null;
            }
        });
        return exists;
    }

    private async Task WithTenantUnitOfWorkAsync(Guid? tenantId, Func<Task> action)
    {
        using (GetRequiredService<ICurrentTenant>().Change(tenantId, tenantId.HasValue ? "tenant" : null))
        {
            await WithUnitOfWorkAsync(action);
        }
    }

    private sealed class RaceAfterFinalCheckCleanupService : NotificationRetentionCleanupService
    {
        private readonly Func<CancellationToken, Task> _beforeDelete;

        public RaceAfterFinalCheckCleanupService(
            IRepository<Notification, Guid> notificationRepository,
            IRepository<UserNotification, Guid> userNotificationRepository,
            IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
            IRepository<NotificationRetentionCleanupCursor, Guid> cleanupCursorRepository,
            IAsyncQueryableExecuter asyncExecuter,
            IDataFilter dataFilter,
            IClock clock,
            IUnitOfWorkManager unitOfWorkManager,
            IOptions<NotificationRetentionOptions> options,
            System.Collections.Generic.IEnumerable<INotificationRetentionDeletionContributor> deletionContributors,
            ILogger<NotificationRetentionCleanupService> logger,
            Func<CancellationToken, Task> beforeDelete)
            : base(
                notificationRepository,
                userNotificationRepository,
                deliveryRepository,
                cleanupCursorRepository,
                asyncExecuter,
                dataFilter,
                clock,
                unitOfWorkManager,
                options,
                deletionContributors,
                logger)
        {
            _beforeDelete = beforeDelete;
        }

        protected override Task BeforeDeleteNotificationAsync(
            Notification notification,
            CancellationToken cancellationToken)
        {
            return _beforeDelete(cancellationToken);
        }
    }
}
