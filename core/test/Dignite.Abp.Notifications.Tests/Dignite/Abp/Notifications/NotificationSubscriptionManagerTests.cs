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
        definitionManager.IsAvailableAsync("n2", userId).Returns(true);

        var manager = new NotificationSubscriptionManager(store, definitionManager, clock);

        await manager.SubscribeToAllAvailableNotificationsAsync(userId);

        await store.Received(1).InsertSubscriptionAsync(
            Arg.Is<NotificationSubscriptionInfo>(s => s.NotificationName == "n2" && s.UserId == userId));
        await store.DidNotReceive().InsertSubscriptionAsync(
            Arg.Is<NotificationSubscriptionInfo>(s => s.NotificationName == "n1"));
    }

    [Fact]
    public async Task Subscribe_is_noop_when_notification_is_not_available()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var clock = Substitute.For<IClock>();
        var userId = Guid.NewGuid();
        definitionManager.IsAvailableAsync("missing", userId).Returns(false);

        var manager = new NotificationSubscriptionManager(store, definitionManager, clock);

        await manager.SubscribeAsync(userId, "missing");

        await store.DidNotReceiveWithAnyArgs().InsertSubscriptionAsync(default!);
    }

    [Fact]
    public async Task Subscribe_is_noop_when_already_subscribed()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var clock = Substitute.For<IClock>();
        var userId = Guid.NewGuid();
        definitionManager.IsAvailableAsync("order.shipped", userId).Returns(true);
        store.IsSubscribedAsync(userId, "order.shipped", null, null).Returns(true);

        var manager = new NotificationSubscriptionManager(store, definitionManager, clock);

        await manager.SubscribeAsync(userId, "order.shipped");

        await store.DidNotReceiveWithAnyArgs().InsertSubscriptionAsync(default!);
    }
}
