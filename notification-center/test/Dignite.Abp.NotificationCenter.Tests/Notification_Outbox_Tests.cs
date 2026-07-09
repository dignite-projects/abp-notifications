using System;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Proves the store-write and the NotificationDeliveryEto publish are atomic via the transactional outbox
/// (roadmap problem C): both commit together, or neither does. EF Core only — the MongoDB provider in
/// this repo does not wire up the transactional outbox, so this scenario is not part of the shared suite.
/// </summary>
public class Notification_Outbox_Tests : NotificationCenterTestBase<AbpNotificationCenterEntityFrameworkCoreTestModule>
{
    private static NotificationInfo NewNotification(Guid id)
    {
        return new NotificationInfo
        {
            Id = id,
            NotificationName = "order.shipped",
            Data = new MessageNotificationData("hi"),
            Severity = NotificationSeverity.Info,
            CreationTime = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task Distribution_persists_the_notification_and_the_outbox_event_together()
    {
        var notificationId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<INotificationDistributor>()
                .DistributeAsync(NewNotification(notificationId), new[] { Guid.NewGuid() });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IRepository<Notification, Guid>>().FindAsync(notificationId)).ShouldNotBeNull();
            ((int)await GetRequiredService<IRepository<OutgoingEventRecord, Guid>>().GetCountAsync())
                .ShouldBeGreaterThan(0);
        });
    }

    [Fact]
    public async Task A_rolled_back_unit_of_work_leaves_neither_the_notification_nor_the_event()
    {
        var notificationId = Guid.NewGuid();

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            await GetRequiredService<INotificationDistributor>()
                .DistributeAsync(NewNotification(notificationId), new[] { Guid.NewGuid() });
            // No CompleteAsync → the whole unit of work rolls back.
        }

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IRepository<Notification, Guid>>().FindAsync(notificationId)).ShouldBeNull();
            ((int)await GetRequiredService<IRepository<OutgoingEventRecord, Guid>>().GetCountAsync()).ShouldBe(0);
        });
    }
}
