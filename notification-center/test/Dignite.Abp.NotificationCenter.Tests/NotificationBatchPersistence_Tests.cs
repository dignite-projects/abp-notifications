using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.NotificationCenter.EntityFrameworkCore;
using Dignite.Abp.Notifications;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

public class NotificationBatchPersistence_Tests :
    NotificationCenterTestBase<AbpNotificationCenterEntityFrameworkCoreTestModule>
{
    [Fact]
    public async Task Ef_batches_flush_and_detach_inbox_entities_inside_one_transaction()
    {
        const int recipientCount = 513;
        var notificationId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            GetRequiredService<INotificationBatchPersistence>()
                .ShouldBeOfType<EfCoreNotificationBatchPersistence>();
            await GetRequiredService<INotificationDistributor>().DistributeAsync(
                new NotificationInfo
                {
                    Id = notificationId,
                    NotificationName = "order.shipped",
                    Data = new MessageNotificationData("bounded"),
                    CreationTime = DateTime.UtcNow
                },
                Enumerable.Range(0, recipientCount).Select(_ => Guid.NewGuid()).ToArray());

            var repository = GetRequiredService<IRepository<UserNotification, Guid>>();
            var dbContext = await ((IEfCoreRepository<UserNotification, Guid>)repository)
                .GetDbContextAsync();
            dbContext.ChangeTracker.Entries<UserNotification>().ShouldBeEmpty();
        }, isTransactional: true);

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IRepository<UserNotification, Guid>>()
                .GetListAsync(row => row.NotificationId == notificationId)).Count.ShouldBe(recipientCount);
        });
    }

    [Fact]
    public async Task Ef_failed_batch_detaches_attempted_entities_before_same_unit_of_work_retry()
    {
        var notificationId = Guid.NewGuid();
        var duplicateUserId = Guid.NewGuid();
        var retryUserId = Guid.NewGuid();
        var failedFirstId = Guid.NewGuid();
        var failedSecondId = Guid.NewGuid();
        var retryId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            var persistence = GetRequiredService<INotificationBatchPersistence>();
            var repository = GetRequiredService<IRepository<UserNotification, Guid>>();

            await Should.ThrowAsync<Exception>(() => persistence.InsertAsync(new[]
            {
                NewUserNotification(failedFirstId, duplicateUserId, notificationId),
                NewUserNotification(failedSecondId, duplicateUserId, notificationId)
            }));

            var dbContext = await ((IEfCoreRepository<UserNotification, Guid>)repository)
                .GetDbContextAsync();
            dbContext.ChangeTracker.Entries<UserNotification>()
                .Any(entry => entry.Entity.Id == failedFirstId || entry.Entity.Id == failedSecondId)
                .ShouldBeFalse();

            await persistence.InsertAsync(new[]
            {
                NewUserNotification(retryId, retryUserId, notificationId)
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var rows = await GetRequiredService<IRepository<UserNotification, Guid>>()
                .GetListAsync(row => row.NotificationId == notificationId);

            rows.Select(row => row.UserId).ShouldContain(retryUserId);
            rows.Count(row => row.UserId == duplicateUserId).ShouldBeLessThanOrEqualTo(1);
        });
    }

    private static UserNotification NewUserNotification(
        Guid id,
        Guid userId,
        Guid notificationId)
    {
        return new UserNotification(
            id,
            userId,
            notificationId,
            UserNotificationState.Unread,
            DateTime.UtcNow,
            tenantId: null);
    }
}
