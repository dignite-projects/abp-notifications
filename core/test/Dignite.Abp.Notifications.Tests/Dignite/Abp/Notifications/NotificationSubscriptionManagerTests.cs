using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using Volo.Abp.Localization;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationSubscriptionManagerTests
{
    [Fact]
    public async Task SubscribeToAllAvailable_subscribes_each_available_notification_not_already_subscribed()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var d1 = new NotificationDefinition("n1", new FixedLocalizableString("n1"));
        var d2 = new NotificationDefinition("n2", new FixedLocalizableString("n2"));
        definitionManager.GetAllAvailableAsync(userId)
            .Returns((IReadOnlyList<NotificationDefinition>)new List<NotificationDefinition> { d1, d2 });

        // Already subscribed to n1, not to n2.
        store.IsSubscribedAsync(userId, "n1", null, null).Returns(true);
        store.IsSubscribedAsync(userId, "n2", null, null).Returns(false);

        var manager = new NotificationSubscriptionManager(store, definitionManager, clock);

        await manager.SubscribeToAllAvailableNotificationsAsync(userId);

        await store.Received(1).InsertSubscriptionAsync(
            Arg.Is<NotificationSubscriptionInfo>(s => s.NotificationName == "n2" && s.UserId == userId));
        await store.DidNotReceive().InsertSubscriptionAsync(
            Arg.Is<NotificationSubscriptionInfo>(s => s.NotificationName == "n1"));
    }
}
