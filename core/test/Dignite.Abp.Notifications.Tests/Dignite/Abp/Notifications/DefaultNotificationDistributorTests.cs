using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Localization;
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
        definitionManager.Get("test").Returns(DefinitionWithChannels());

        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(ci => published = ci.Arg<NotificationDeliveryEto>());

        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);

        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var notification = new NotificationInfo
        {
            Id = Guid.NewGuid(),
            NotificationName = "test",
            CreationTime = DateTime.UtcNow,
            TenantId = tenantId
        };

        await distributor.DistributeAsync(notification, new[] { u1, u2, u3 }, new[] { u2 });

        published.ShouldNotBeNull();
        published!.UserIds.Length.ShouldBe(2);
        published.UserIds.ShouldContain(u1);
        published.UserIds.ShouldContain(u3);
        published.UserIds.ShouldNotContain(u2);
        published.TenantId.ShouldBe(tenantId);
        ((IEventDataMayHaveTenantId)published).IsMultiTenant(out var eventTenantId).ShouldBeTrue();
        eventTenantId.ShouldBe(tenantId);

        await store.Received(1).InsertNotificationAsync(Arg.Any<NotificationInfo>());
        // The distributor never switches tenants; it just carries the notification's tenant onto every row it writes
        // and onto the ETO. Restoring the tenant is NotificationDistributionJob's job on the background path.
        await store.Received(2).InsertUserNotificationAsync(Arg.Is<UserNotificationInfo>(x => x.TenantId == tenantId));
    }

    [Fact]
    public async Task Resolves_subscribers_and_keeps_only_available_ones()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());

        var available = Guid.NewGuid();
        var notAvailable = Guid.NewGuid();
        store.GetSubscriptionsAsync("test", null, null).Returns(new List<NotificationSubscriptionInfo>
        {
            new() { UserId = available, NotificationName = "test" },
            new() { UserId = notAvailable, NotificationName = "test" }
        });
        definitionManager.IsAvailableAsync("test", available).Returns(true);
        definitionManager.IsAvailableAsync("test", notAvailable).Returns(false);

        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(ci => published = ci.Arg<NotificationDeliveryEto>());

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
        definitionManager.Get("test").Returns(DefinitionWithChannels());

        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);
        var notification = new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" };

        // Explicit single user, then excluded → empty target set.
        var user = Guid.NewGuid();
        await distributor.DistributeAsync(notification, new[] { user }, new[] { user });

        await store.DidNotReceiveWithAnyArgs().InsertNotificationAsync(default!);
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Fact]
    public async Task No_channels_persists_without_publishing_a_delivery_event()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("test").Returns(new NotificationDefinition("test", new FixedLocalizableString("Test")));

        var userId = Guid.NewGuid();
        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);
        var notification = new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" };

        await distributor.DistributeAsync(notification, new[] { userId });

        await store.Received(1).InsertNotificationAsync(notification);
        await store.Received(1).InsertUserNotificationAsync(Arg.Is<UserNotificationInfo>(x => x.UserId == userId));
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Fact]
    public async Task No_channels_throw_when_there_is_no_inbox_store()
    {
        var store = new NullNotificationStore();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("test").Returns(new NotificationDefinition("test", new FixedLocalizableString("Test")));

        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);
        var notification = new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" };

        await Should.ThrowAsync<AbpException>(() => distributor.DistributeAsync(notification, new[] { Guid.NewGuid() }));

        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    private static NotificationDefinition DefinitionWithChannels()
    {
        return new NotificationDefinition("test", new FixedLocalizableString("Test")).UseChannels("Test");
    }
}
