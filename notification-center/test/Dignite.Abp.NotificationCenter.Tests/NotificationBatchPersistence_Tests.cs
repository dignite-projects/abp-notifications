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
}
