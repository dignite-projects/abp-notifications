using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationDistributorTests
{
    [Fact]
    public async Task Publishes_eto_once_per_distinct_explicit_user_minus_excluded()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());
        definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(true);

        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(ci => published = ci.Arg<NotificationDeliveryEto>());

        var distributor = CreateDistributor(store, definitionManager, eventBus);

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

        await distributor.DistributeAsync(notification, new[] { u1, u1, u2, u3, u3 }, new[] { u2 });

        published.ShouldNotBeNull();
        published!.UserIds.Length.ShouldBe(2);
        published.UserIds.ShouldContain(u1);
        published.UserIds.ShouldContain(u3);
        published.UserIds.ShouldNotContain(u2);
        published.TenantId.ShouldBe(tenantId);
        ((IEventDataMayHaveTenantId)published).IsMultiTenant(out var eventTenantId).ShouldBeTrue();
        eventTenantId.ShouldBe(tenantId);

        await store.Received(1).InsertNotificationAsync(Arg.Any<NotificationInfo>());
        // The distributor also carries the notification tenant explicitly on every persisted per-user row and ETO.
        await store.Received(2).InsertUserNotificationAsync(Arg.Is<UserNotificationInfo>(x => x.TenantId == tenantId));
    }

    [Fact]
    public async Task Empty_explicit_recipient_list_does_not_resolve_subscriptions_or_produce_side_effects()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());

        var subscribedUser = Guid.NewGuid();
        store.GetSubscriptionsAsync("test", null, null).Returns(new List<NotificationSubscriptionInfo>
        {
            new() { UserId = subscribedUser, NotificationName = "test" }
        });
        definitionManager.IsAvailableAsync("test", subscribedUser).Returns(true);

        var distributor = CreateDistributor(store, definitionManager, eventBus);
        var notification = new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" };

        await distributor.DistributeAsync(notification, Array.Empty<Guid>());

        definitionManager.DidNotReceiveWithAnyArgs().Get(default!);
        await store.DidNotReceiveWithAnyArgs().GetSubscriptionsAsync(default!, default, default);
        await store.DidNotReceiveWithAnyArgs().InsertNotificationAsync(default!);
        await store.DidNotReceiveWithAnyArgs().InsertUserNotificationAsync(default!);
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
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

        var distributor = CreateDistributor(store, definitionManager, eventBus);
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

        var distributor = CreateDistributor(store, definitionManager, eventBus);
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
        definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(true);

        var userId = Guid.NewGuid();
        var distributor = CreateDistributor(store, definitionManager, eventBus);
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

        var distributor = CreateDistributor(store, definitionManager, eventBus);
        var notification = new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" };

        await Should.ThrowAsync<AbpException>(() => distributor.DistributeAsync(notification, new[] { Guid.NewGuid() }));

        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Fact]
    public async Task Carries_entity_identity_onto_the_delivery_event_and_the_per_user_view()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());
        definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(true);

        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(ci => published = ci.Arg<NotificationDeliveryEto>());

        var distributor = CreateDistributor(store, definitionManager, eventBus);

        await distributor.DistributeAsync(
            new NotificationInfo
            {
                Id = Guid.NewGuid(),
                NotificationName = "test",
                EntityTypeName = "Demo.Order",
                EntityId = "1001"
            },
            new[] { Guid.NewGuid(), Guid.NewGuid() });

        // Without this, a notifier cannot tell which entity a notification is about.
        published.ShouldNotBeNull();
        published!.EntityTypeName.ShouldBe("Demo.Order");
        published.EntityId.ShouldBe("1001");

        // The per-user view forwards entity identity but must still drop the recipient list (invariants §4).
        var delivery = NotificationDelivery.FromEto(published);
        delivery.EntityTypeName.ShouldBe("Demo.Order");
        delivery.EntityId.ShouldBe("1001");
        typeof(NotificationDelivery).GetProperty("UserIds").ShouldBeNull();
    }

    [Fact]
    public async Task Explicit_recipients_are_filtered_by_the_same_definition_eligibility_as_subscribers()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());

        var eligible = Guid.NewGuid();
        var denied = Guid.NewGuid();
        definitionManager.IsAvailableAsync("test", eligible).Returns(true);
        definitionManager.IsAvailableAsync("test", denied).Returns(false);

        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(bus => bus.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(call => published = call.Arg<NotificationDeliveryEto>());

        var distributor = CreateDistributor(store, definitionManager, eventBus);
        await distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" },
            new[] { eligible, denied });

        published.ShouldNotBeNull();
        published!.UserIds.ShouldBe(new[] { eligible });
        await store.DidNotReceive().InsertUserNotificationAsync(
            Arg.Is<UserNotificationInfo>(row => row.UserId == denied));
    }

    [Fact]
    public async Task Explicit_and_subscription_candidates_flow_through_the_same_batch_evaluator()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var evaluator = Substitute.For<INotificationRecipientEligibilityEvaluator>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());

        var explicitUser = Guid.NewGuid();
        var subscribedUser = Guid.NewGuid();
        store.GetSubscriptionsAsync("test", null, null).Returns(new List<NotificationSubscriptionInfo>
        {
            new() { UserId = subscribedUser, NotificationName = "test" }
        });
        evaluator.EvaluateAsync(
                "test",
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            .Returns(call =>
            {
                var candidates = call.ArgAt<IReadOnlyCollection<Guid>>(1).ToList();
                return new NotificationRecipientEligibilityResult(candidates, Array.Empty<Guid>());
            });

        var distributor = CreateDistributor(store, definitionManager, eventBus, evaluator);

        await distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" },
            new[] { explicitUser });
        await distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" });

        await evaluator.Received(1).EvaluateAsync(
            "test",
            Arg.Is<IReadOnlyCollection<Guid>>(users => users.SequenceEqual(new[] { explicitUser })),
            null,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);
        await evaluator.Received(1).EvaluateAsync(
            "test",
            Arg.Is<IReadOnlyCollection<Guid>>(users => users.SequenceEqual(new[] { subscribedUser })),
            null,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);
    }

    [Fact]
    public async Task Named_bypass_is_forwarded_only_for_explicit_recipients()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var evaluator = Substitute.For<INotificationRecipientEligibilityEvaluator>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());
        var userId = Guid.NewGuid();
        evaluator.EvaluateAsync(
                "test",
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                NotificationRecipientEligibilityMode.BypassDefinitionRequirements)
            .Returns(new NotificationRecipientEligibilityResult(new[] { userId }, Array.Empty<Guid>()));

        var logger = Substitute.For<ILogger<DefaultNotificationDistributor>>();
        var distributor = CreateDistributor(store, definitionManager, eventBus, evaluator, logger: logger);

        await distributor.DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" },
            new[] { userId });

        await evaluator.Received(1).EvaluateAsync(
            "test",
            Arg.Is<IReadOnlyCollection<Guid>>(users => users.SequenceEqual(new[] { userId })),
            null,
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements);
        await definitionManager.DidNotReceiveWithAnyArgs().IsAvailableAsync(default!, default);
        logger.ReceivedCalls().Any(call =>
            Equals(call.GetArguments().FirstOrDefault(), LogLevel.Warning)).ShouldBeTrue();
    }

    [Fact]
    public async Task Direct_distribution_rejects_definition_contract_mismatch_before_side_effects()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("typed").Returns(
            new NotificationDefinition("typed", new FixedLocalizableString("Typed"))
                .WithPayload<MessageNotificationData>()
                .UseChannels("Test"));
        var dataOptions = new NotificationDataOptions();
        dataOptions.Add<MessageNotificationData>();
        dataOptions.Add<LocalizableMessageNotificationData>();
        var distributor = CreateDistributor(
            store,
            definitionManager,
            eventBus,
            dataTypeRegistry: new NotificationDataTypeRegistry(Options.Create(dataOptions)));

        var exception = await Should.ThrowAsync<AbpException>(() => distributor.DistributeAsync(
            new NotificationInfo
            {
                Id = Guid.NewGuid(),
                NotificationName = "typed",
                Data = new LocalizableMessageNotificationData("Test", "Wrong"),
                CreationTime = DateTime.UtcNow
            },
            new[] { Guid.NewGuid() }));

        exception.Message.ShouldContain("Dignite.Message");
        exception.Message.ShouldContain("Dignite.LocalizableMessage");
        await store.DidNotReceiveWithAnyArgs().GetSubscriptionsAsync(default!, default, default);
        await store.DidNotReceiveWithAnyArgs().InsertNotificationAsync(default!);
        await store.DidNotReceiveWithAnyArgs().InsertUserNotificationAsync(default!);
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Fact]
    public async Task Direct_distribution_rejects_partial_raw_entity_identity_for_an_opted_in_contract()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        definitionManager.Get("entity-aware").Returns(
            new NotificationDefinition("entity-aware", new FixedLocalizableString("Entity aware"))
                .WithEntityContract(NotificationEntityRequirement.Optional, "Demo.Order")
                .UseChannels("Test"));
        var distributor = CreateDistributor(store, definitionManager, eventBus);

        var exception = await Should.ThrowAsync<AbpException>(() => distributor.DistributeAsync(
            new NotificationInfo
            {
                Id = Guid.NewGuid(),
                NotificationName = "entity-aware",
                EntityTypeName = "Demo.Order",
                EntityId = null,
                CreationTime = DateTime.UtcNow
            },
            new[] { Guid.NewGuid() }));

        exception.Message.ShouldContain("incomplete entity identity");
        await store.DidNotReceiveWithAnyArgs().InsertNotificationAsync(default!);
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Entire_distribution_uses_the_notification_tenant_or_host_and_restores_the_caller_context(
        bool hostNotification)
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var currentTenant = new TestCurrentTenant();
        Guid? notificationTenantId = hostNotification ? null : Guid.NewGuid();
        var callerTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantsSeen = new List<Guid?>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());
        store.GetSubscriptionsAsync("test", null, null).Returns(_ =>
        {
            tenantsSeen.Add(currentTenant.Id);
            return new List<NotificationSubscriptionInfo>
            {
                new() { UserId = userId, NotificationName = "test" }
            };
        });
        definitionManager.IsAvailableAsync("test", userId).Returns(_ =>
        {
            tenantsSeen.Add(currentTenant.Id);
            return true;
        });
        store.When(candidate => candidate.InsertNotificationAsync(Arg.Any<NotificationInfo>()))
            .Do(_ => tenantsSeen.Add(currentTenant.Id));
        store.When(candidate => candidate.InsertUserNotificationAsync(Arg.Any<UserNotificationInfo>()))
            .Do(_ => tenantsSeen.Add(currentTenant.Id));
        eventBus.WhenForAnyArgs(candidate => candidate.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(_ => tenantsSeen.Add(currentTenant.Id));
        var distributor = CreateDistributor(
            store,
            definitionManager,
            eventBus,
            currentTenant: currentTenant);

        using (currentTenant.Change(callerTenantId, "caller"))
        {
            await distributor.DistributeAsync(new NotificationInfo
            {
                Id = Guid.NewGuid(),
                NotificationName = "test",
                TenantId = notificationTenantId
            });

            currentTenant.Id.ShouldBe(callerTenantId);
        }

        tenantsSeen.Count.ShouldBe(5);
        tenantsSeen.ShouldAllBe(tenantId => tenantId == notificationTenantId);
        currentTenant.Id.ShouldBeNull();
    }

    private static DefaultNotificationDistributor CreateDistributor(
        INotificationStore store,
        INotificationDefinitionManager definitionManager,
        IDistributedEventBus eventBus,
        INotificationRecipientEligibilityEvaluator? evaluator = null,
        ICurrentTenant? currentTenant = null,
        ILogger<DefaultNotificationDistributor>? logger = null,
        INotificationDataTypeRegistry? dataTypeRegistry = null)
    {
        currentTenant ??= new TestCurrentTenant();
        evaluator ??= new DefaultNotificationRecipientEligibilityEvaluator(
            definitionManager,
            currentTenant,
            NullLogger<DefaultNotificationRecipientEligibilityEvaluator>.Instance);

        return new DefaultNotificationDistributor(
            store,
            definitionManager,
            eventBus,
            evaluator,
            currentTenant,
            logger ?? NullLogger<DefaultNotificationDistributor>.Instance,
            dataTypeRegistry ?? new NotificationDataTypeRegistry(
                Options.Create(new NotificationDataOptions())));
    }

    private static NotificationDefinition DefinitionWithChannels()
    {
        return new NotificationDefinition("test", new FixedLocalizableString("Test")).UseChannels("Test");
    }
}
