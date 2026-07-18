using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
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

        var result = await CleanupAsync(now);

        result.DeletedUserNotifications.ShouldBe(1);
        result.DeletedDeliveries.ShouldBe(1);
        result.DeletedNotifications.ShouldBe(3);
        result.SkippedNotifications.ShouldBe(2);

        (await ExistsAsync<Notification>(orphanId)).ShouldBeFalse();
        (await ExistsAsync<UserNotification>(readUserNotificationId)).ShouldBeFalse();
        (await ExistsAsync<Notification>(readNotificationId)).ShouldBeFalse();
        (await ExistsAsync<UserNotification>(unreadUserNotificationId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(unreadNotificationId)).ShouldBeTrue();
        (await ExistsAsync<NotificationDeliveryRecord>(pendingDeliveryId)).ShouldBeTrue();
        (await ExistsAsync<Notification>(pendingNotificationId)).ShouldBeTrue();
        (await ExistsAsync<NotificationDeliveryRecord>(terminalDeliveryId)).ShouldBeFalse();
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
        await InsertNotificationAsync(retainedId, old);
        await InsertNotificationAsync(failingId, old.AddMinutes(1));
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
    public async Task Concurrent_reference_created_between_archive_and_delete_protects_the_base_notification()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-120);
        var notificationId = Guid.NewGuid();
        var userNotificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await InsertNotificationAsync(notificationId, old);
        TestNotificationRetentionDeletionContributor.Callbacks[notificationId] = async (candidate, cancellationToken) =>
        {
            await InsertUserNotificationAsync(
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
    public async Task Tenant_local_reference_checks_do_not_mix_host_or_other_tenant_records()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var old = now.AddDays(-120);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantANotificationId = Guid.NewGuid();
        var tenantBInboxRowId = Guid.NewGuid();
        await InsertNotificationAsync(tenantANotificationId, old, tenantA);
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
    public async Task Batch_size_limits_each_pass_and_next_pass_continues_from_remaining_candidates()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await InsertNotificationAsync(firstId, now.AddDays(-121));
        await InsertNotificationAsync(secondId, now.AddDays(-120));

        var firstPass = await CleanupAsync(now, batchSize: 1);

        firstPass.ScannedNotifications.ShouldBe(1);
        firstPass.DeletedNotifications.ShouldBe(1);
        (await ExistingNotificationIdsAsync(firstId, secondId)).Count.ShouldBe(1);

        var secondPass = await CleanupAsync(now, batchSize: 1);

        secondPass.ScannedNotifications.ShouldBe(1);
        secondPass.DeletedNotifications.ShouldBe(1);
        (await ExistingNotificationIdsAsync(firstId, secondId)).ShouldBeEmpty();
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

    private async Task InsertNotificationAsync(Guid notificationId, DateTime creationTime, Guid? tenantId = null)
    {
        await WithTenantUnitOfWorkAsync(tenantId, async () =>
        {
            await GetRequiredService<IRepository<Notification, Guid>>().InsertAsync(new Notification(
                notificationId,
                "order.shipped",
                data: null,
                entityTypeName: null,
                entityId: null,
                NotificationSeverity.Info,
                creationTime,
                tenantId));
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

    private async Task<System.Collections.Generic.List<Guid>> ExistingNotificationIdsAsync(params Guid[] ids)
    {
        System.Collections.Generic.List<Guid>? existing = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (GetRequiredService<IDataFilter>().Disable<IMultiTenant>())
            {
                var repository = GetRequiredService<IRepository<Notification, Guid>>();
                var rows = await repository.GetListAsync(notification => ids.Contains(notification.Id));
                existing = rows.Select(notification => notification.Id).ToList();
            }
        });
        return existing!;
    }

    private async Task WithTenantUnitOfWorkAsync(Guid? tenantId, Func<Task> action)
    {
        using (GetRequiredService<ICurrentTenant>().Change(tenantId, tenantId.HasValue ? "tenant" : null))
        {
            await WithUnitOfWorkAsync(action);
        }
    }
}
