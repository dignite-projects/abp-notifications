using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Volo.Abp.EventBus.Distributed;
using Xunit;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationDistributorTests
{
    [Fact]
    public async Task Publishes_eto_to_explicit_users_minus_excluded()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();

        RealTimeNotifyEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<RealTimeNotifyEto>()))
            .Do(ci => published = ci.Arg<RealTimeNotifyEto>());

        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);

        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();
        var notification = new NotificationInfo
        {
            Id = Guid.NewGuid(),
            NotificationName = "test",
            CreationTime = DateTime.UtcNow
        };

        await distributor.DistributeAsync(notification, new[] { u1, u2, u3 }, new[] { u2 });

        published.ShouldNotBeNull();
        published!.UserIds.Length.ShouldBe(2);
        published.UserIds.ShouldContain(u1);
        published.UserIds.ShouldContain(u3);
        published.UserIds.ShouldNotContain(u2);

        await store.Received(1).InsertNotificationAsync(Arg.Any<NotificationInfo>());
        await store.Received(2).InsertUserNotificationAsync(Arg.Any<UserNotificationInfo>());
    }

    [Fact]
    public async Task Resolves_subscribers_and_keeps_only_available_ones()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();

        var available = Guid.NewGuid();
        var notAvailable = Guid.NewGuid();
        store.GetSubscriptionsAsync("test", null, null).Returns(new List<NotificationSubscriptionInfo>
        {
            new() { UserId = available, NotificationName = "test" },
            new() { UserId = notAvailable, NotificationName = "test" }
        });
        definitionManager.IsAvailableAsync("test", available).Returns(true);
        definitionManager.IsAvailableAsync("test", notAvailable).Returns(false);

        RealTimeNotifyEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<RealTimeNotifyEto>()))
            .Do(ci => published = ci.Arg<RealTimeNotifyEto>());

        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);
        var notification = new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" };

        // No explicit userIds → subscription-driven path, filtered by availability.
        await distributor.DistributeAsync(notification);

        published.ShouldNotBeNull();
        published!.UserIds.ShouldBe(new[] { available });
    }

    [Fact]
    public async Task Does_nothing_when_there_are_no_target_users()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();

        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);
        var notification = new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" };

        // Explicit single user, then excluded → empty target set.
        var user = Guid.NewGuid();
        await distributor.DistributeAsync(notification, new[] { user }, new[] { user });

        await store.DidNotReceiveWithAnyArgs().InsertNotificationAsync(default!);
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<RealTimeNotifyEto>());
    }
}
