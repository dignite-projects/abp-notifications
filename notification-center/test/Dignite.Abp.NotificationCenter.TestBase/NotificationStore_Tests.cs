using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Provider-agnostic <see cref="INotificationStore"/> scenarios. The concrete EF Core and MongoDB
/// test projects each derive a thin subclass bound to their own startup module, so these exact
/// assertions run against both persistence providers.
/// </summary>
public abstract class NotificationStore_Tests<TStartupModule> : NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private async Task InsertAsync(Guid notificationId, Guid userId, NotificationData data,
        UserNotificationState state = UserNotificationState.Unread)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<INotificationStore>();
            var info = new NotificationInfo
            {
                Id = notificationId,
                NotificationName = "order.shipped",
                Data = data,
                Severity = NotificationSeverity.Success,
                CreationTime = DateTime.UtcNow
            };
            await store.InsertNotificationAsync(info);
            await store.InsertUserNotificationAsync(new UserNotificationInfo
            {
                UserId = userId,
                NotificationId = notificationId,
                State = state,
                CreationTime = info.CreationTime
            });
        });
    }

    [Fact]
    public async Task Round_trips_custom_notification_data_through_the_store()
    {
        var notificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await InsertAsync(notificationId, userId,
            new OrderShippedNotificationData { OrderNumber = "SO-1001", ItemCount = 3 });

        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<INotificationStore>();
            var list = await store.GetUserNotificationsAsync(userId);

            list.Count.ShouldBe(1);
            var row = list.Single();
            row.Notification.NotificationName.ShouldBe("order.shipped");
            var data = row.Notification.Data.ShouldBeOfType<OrderShippedNotificationData>();
            data.OrderNumber.ShouldBe("SO-1001");
            data.ItemCount.ShouldBe(3);
            row.UserNotification.State.ShouldBe(UserNotificationState.Unread);
        });
    }

    [Fact]
    public async Task Persisted_data_uses_the_discriminator_and_no_assembly_qualified_name()
    {
        var notificationId = Guid.NewGuid();
        await InsertAsync(notificationId, Guid.NewGuid(),
            new OrderShippedNotificationData { OrderNumber = "SO-9", ItemCount = 1 });

        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<IRepository<Notification, Guid>>();
            var entity = await repository.GetAsync(notificationId);

            entity.Data.ShouldNotBeNull();
            entity.Data!.ShouldContain("\"type\":\"Test.OrderShipped\"");
            entity.Data.ShouldNotContain("Version=");
            entity.Data.ShouldNotContain("OrderShippedNotificationData");
        });
    }

    [Fact]
    public async Task Filters_inbox_by_state_and_counts()
    {
        var userId = Guid.NewGuid();
        await InsertAsync(Guid.NewGuid(), userId, new MessageNotificationData("a"), UserNotificationState.Unread);
        await InsertAsync(Guid.NewGuid(), userId, new MessageNotificationData("b"), UserNotificationState.Read);

        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<INotificationStore>();

            (await store.GetUserNotificationCountAsync(userId)).ShouldBe(2);
            (await store.GetUserNotificationCountAsync(userId, UserNotificationState.Unread)).ShouldBe(1);

            var unread = await store.GetUserNotificationsAsync(userId, UserNotificationState.Unread);
            unread.Count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Subscribes_and_queries_subscriptions()
    {
        var userId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<INotificationStore>();
            await store.InsertSubscriptionAsync(new NotificationSubscriptionInfo
            {
                UserId = userId,
                NotificationName = "order.shipped"
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<INotificationStore>();

            (await store.IsSubscribedAsync(userId, "order.shipped", null, null)).ShouldBeTrue();
            (await store.GetSubscriptionsAsync("order.shipped", null, null))
                .ShouldContain(s => s.UserId == userId);
            (await store.GetSubscriptionsAsync(userId)).Count.ShouldBe(1);
        });
    }
}
