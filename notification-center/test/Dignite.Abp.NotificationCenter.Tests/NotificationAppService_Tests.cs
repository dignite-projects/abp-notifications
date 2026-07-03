using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Shouldly;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

public class NotificationAppService_Tests : NotificationCenterTestBase
{
    private readonly Guid _userId = Guid.NewGuid();

    private async Task<Guid> SeedNotificationAsync(
        NotificationData data, UserNotificationState state = UserNotificationState.Unread)
    {
        var notificationId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<INotificationStore>();
            await store.InsertNotificationAsync(new NotificationInfo
            {
                Id = notificationId,
                NotificationName = "order.shipped",
                Data = data,
                Severity = NotificationSeverity.Info,
                CreationTime = DateTime.UtcNow
            });
            await store.InsertUserNotificationAsync(new UserNotificationInfo
            {
                UserId = _userId,
                NotificationId = notificationId,
                State = state,
                CreationTime = DateTime.UtcNow
            });
        });
        return notificationId;
    }

    [Fact]
    public async Task GetList_returns_typed_data_and_a_read_time_localized_display_name()
    {
        await SeedNotificationAsync(new OrderShippedNotificationData { OrderNumber = "SO-1", ItemCount = 2 });

        using (ChangeCurrentUser(_userId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var appService = GetRequiredService<INotificationAppService>();
                var result = await appService.GetListAsync(new GetUserNotificationListInput());

                result.TotalCount.ShouldBe(1);
                var dto = result.Items.Single();
                dto.NotificationName.ShouldBe("order.shipped");
                dto.NotificationDisplayName.ShouldBe("Order Shipped");
                dto.Data.ShouldBeOfType<OrderShippedNotificationData>().OrderNumber.ShouldBe("SO-1");
                dto.State.ShouldBe(UserNotificationState.Unread);
            });
        }
    }

    [Fact]
    public async Task Marking_as_read_updates_state_and_count()
    {
        var notificationId = await SeedNotificationAsync(new MessageNotificationData("hi"));

        using (ChangeCurrentUser(_userId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var appService = GetRequiredService<INotificationAppService>();
                (await appService.GetCountAsync(UserNotificationState.Unread)).ShouldBe(1);
                await appService.MarkAsReadAsync(notificationId);
            });

            await WithUnitOfWorkAsync(async () =>
            {
                var appService = GetRequiredService<INotificationAppService>();
                (await appService.GetCountAsync(UserNotificationState.Unread)).ShouldBe(0);
                (await appService.GetCountAsync(UserNotificationState.Read)).ShouldBe(1);
            });
        }
    }

    [Fact]
    public async Task Subscribing_reflects_in_available_subscriptions()
    {
        using (ChangeCurrentUser(_userId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await GetRequiredService<INotificationAppService>().SubscribeAsync("order.shipped");
            });

            await WithUnitOfWorkAsync(async () =>
            {
                var subscriptions = await GetRequiredService<INotificationAppService>().GetSubscriptionsAsync();
                var subscription = subscriptions.Items.Single(s => s.NotificationName == "order.shipped");
                subscription.IsSubscribed.ShouldBeTrue();
                subscription.DisplayName.ShouldBe("Order Shipped");
            });
        }
    }
}
