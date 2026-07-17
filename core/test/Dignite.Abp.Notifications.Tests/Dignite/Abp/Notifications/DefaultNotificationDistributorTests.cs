using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
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
    public async Task Bounded_pipeline_respects_exact_batch_limits_and_emits_progress_metrics()
    {
        var store = Substitute.For<INotificationStore, IBatchedNotificationStore>();
        var batchedStore = (IBatchedNotificationStore)store;
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var evaluator = Substitute.For<INotificationRecipientEligibilityEvaluator>();
        var notificationName = $"batch-metrics-{Guid.NewGuid():N}";
        definitionManager.Get(notificationName).Returns(
            new NotificationDefinition(notificationName, new FixedLocalizableString("Batch")).UseChannels("Test"));

        var candidateBatchSizes = new List<int>();
        evaluator.EvaluateAsync(
                notificationName,
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            .Returns(call =>
            {
                var candidates = call.ArgAt<IReadOnlyCollection<Guid>>(1).ToArray();
                candidateBatchSizes.Add(candidates.Length);
                return new NotificationRecipientEligibilityResult(candidates, Array.Empty<Guid>());
            });

        var writeBatches = new List<Guid[]>();
        batchedStore.When(storeCapability => storeCapability.InsertUserNotificationsAsync(
                Arg.Any<IReadOnlyCollection<UserNotificationInfo>>(),
                Arg.Any<CancellationToken>()))
            .Do(call => writeBatches.Add(call
                .ArgAt<IReadOnlyCollection<UserNotificationInfo>>(0)
                .Select(row => row.UserId)
                .ToArray()));

        var deliveryBatches = new List<Guid[]>();
        eventBus.WhenForAnyArgs(bus => bus.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(call => deliveryBatches.Add(call.Arg<NotificationDeliveryEto>().UserIds.ToArray()));

        long candidateMetric = 0;
        long eligibleMetric = 0;
        long filteredMetric = 0;
        long batchMetric = 0;
        var durationMeasurements = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == NotificationDistributionMetrics.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (!HasTag(tags, "notification.name", notificationName))
            {
                return;
            }

            switch (instrument.Name)
            {
                case NotificationDistributionMetrics.CandidateCountName:
                    candidateMetric += measurement;
                    break;
                case NotificationDistributionMetrics.EligibleCountName:
                    eligibleMetric += measurement;
                    break;
                case NotificationDistributionMetrics.FilteredCountName:
                    filteredMetric += measurement;
                    break;
                case NotificationDistributionMetrics.BatchCountName:
                    batchMetric += measurement;
                    break;
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (instrument.Name == NotificationDistributionMetrics.DurationName &&
                HasTag(tags, "notification.name", notificationName))
            {
                durationMeasurements++;
            }
        });
        listener.Start();

        var options = new NotificationOptions
        {
            RecipientBatchSize = 128,
            UserNotificationWriteBatchSize = 64,
            DeliveryEventRecipientLimit = 50
        };
        var distributor = CreateDistributor(
            store,
            definitionManager,
            eventBus,
            evaluator,
            options: options);
        var distinctUserIds = Enumerable.Range(0, 512)
            .Select(_ => Guid.NewGuid())
            .Append(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"))
            .ToArray();
        var userIds = distinctUserIds
            .Concat(new[] { distinctUserIds[0], distinctUserIds[255], distinctUserIds[^1] })
            .ToArray();

        await distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = notificationName },
            userIds,
            new[] { distinctUserIds[^1] });

        // The final one-recipient candidate batch is removed by caller exclusion before policy evaluation.
        candidateBatchSizes.ShouldBe(new[] { 128, 128, 128, 128 });
        writeBatches.Count.ShouldBe(8);
        writeBatches.ShouldAllBe(batch => batch.Length == 64);
        deliveryBatches.Select(batch => batch.Length)
            .ShouldBe(new[] { 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 12 });
        writeBatches.SelectMany(batch => batch).Distinct().Count().ShouldBe(512);
        deliveryBatches.SelectMany(batch => batch).Distinct().Count().ShouldBe(512);
        await batchedStore.Received(1).InsertNotificationAsync(
            Arg.Any<NotificationInfo>(),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().InsertUserNotificationAsync(default!);

        candidateMetric.ShouldBe(513);
        eligibleMetric.ShouldBe(512);
        filteredMetric.ShouldBe(1);
        batchMetric.ShouldBe(24);
        durationMeasurements.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_is_observed_after_a_completed_delivery_batch()
    {
        var store = Substitute.For<INotificationStore, IBatchedNotificationStore>();
        var batchedStore = (IBatchedNotificationStore)store;
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var evaluator = Substitute.For<INotificationRecipientEligibilityEvaluator>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());
        evaluator.EvaluateAsync(
                "test",
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            .Returns(call =>
            {
                var candidates = call.ArgAt<IReadOnlyCollection<Guid>>(1).ToArray();
                return new NotificationRecipientEligibilityResult(candidates, Array.Empty<Guid>());
            });

        var persistedRecipients = new List<Guid>();
        batchedStore.When(storeCapability => storeCapability.InsertUserNotificationsAsync(
                Arg.Any<IReadOnlyCollection<UserNotificationInfo>>(),
                Arg.Any<CancellationToken>()))
            .Do(call => persistedRecipients.AddRange(call
                .ArgAt<IReadOnlyCollection<UserNotificationInfo>>(0)
                .Select(row => row.UserId)));
        using var cancellation = new CancellationTokenSource();
        var deliveryCount = 0;
        eventBus.WhenForAnyArgs(bus => bus.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(_ =>
            {
                deliveryCount++;
                cancellation.Cancel();
            });
        var distributor = CreateDistributor(
            store,
            definitionManager,
            eventBus,
            evaluator,
            options: new NotificationOptions
            {
                RecipientBatchSize = 2,
                UserNotificationWriteBatchSize = 2,
                DeliveryEventRecipientLimit = 2
            });

        await Should.ThrowAsync<OperationCanceledException>(() => distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" },
            Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray(),
            null,
            cancellation.Token));

        deliveryCount.ShouldBe(1);
        persistedRecipients.Count.ShouldBe(2);
        await evaluator.Received(1).EvaluateAsync(
            "test",
            Arg.Any<IReadOnlyCollection<Guid>>(),
            null,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);
    }

    [Fact]
    public async Task Prepared_explicit_batch_skips_the_shared_notification_insert()
    {
        var store = Substitute.For<INotificationStore, IBatchedNotificationStore>();
        var batchedStore = (IBatchedNotificationStore)store;
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var evaluator = Substitute.For<INotificationRecipientEligibilityEvaluator>();
        definitionManager.Get("test").Returns(DefinitionWithChannels());
        evaluator.EvaluateAsync(
                "test",
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            .Returns(call =>
            {
                var candidates = call.ArgAt<IReadOnlyCollection<Guid>>(1).ToArray();
                return new NotificationRecipientEligibilityResult(candidates, Array.Empty<Guid>());
            });
        var distributor = CreateDistributor(store, definitionManager, eventBus, evaluator);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await distributor.DistributePreparedAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" },
            users,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements,
            CancellationToken.None);

        await batchedStore.DidNotReceiveWithAnyArgs().InsertNotificationAsync(
            default!,
            default);
        await batchedStore.Received(1).InsertUserNotificationsAsync(
            Arg.Is<IReadOnlyCollection<UserNotificationInfo>>(rows =>
                rows.Select(row => row.UserId).SequenceEqual(users)),
            CancellationToken.None);
        await eventBus.Received(1).PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Fact]
    public async Task Failure_metric_identifies_the_failed_pipeline_stage()
    {
        var store = Substitute.For<INotificationStore, IBatchedNotificationStore>();
        var batchedStore = (IBatchedNotificationStore)store;
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var evaluator = Substitute.For<INotificationRecipientEligibilityEvaluator>();
        var notificationName = $"batch-failure-{Guid.NewGuid():N}";
        definitionManager.Get(notificationName).Returns(
            new NotificationDefinition(notificationName, new FixedLocalizableString("Failure")).UseChannels("Test"));
        evaluator.EvaluateAsync(
                notificationName,
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            .Returns(call =>
            {
                var candidates = call.ArgAt<IReadOnlyCollection<Guid>>(1).ToArray();
                return new NotificationRecipientEligibilityResult(candidates, Array.Empty<Guid>());
            });
        batchedStore.InsertUserNotificationsAsync(
                Arg.Any<IReadOnlyCollection<UserNotificationInfo>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("store failed"));

        long failures = 0;
        string? failedStage = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Name == NotificationDistributionMetrics.FailureCountName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            if (HasTag(tags, "notification.name", notificationName))
            {
                failures += measurement;
                failedStage = GetTag(tags, "distribution.stage");
            }
        });
        listener.Start();
        var distributor = CreateDistributor(store, definitionManager, eventBus, evaluator);

        await Should.ThrowAsync<InvalidOperationException>(() => distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = notificationName },
            new[] { Guid.NewGuid() }));

        failures.ShouldBe(1);
        failedStage.ShouldBe("persistence");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(NotificationOptions.MaxDistributionBatchSize + 1)]
    public void Invalid_distribution_batch_size_fails_before_distribution(int batchSize)
    {
        var options = new NotificationOptions { RecipientBatchSize = batchSize };

        var exception = Should.Throw<InvalidOperationException>(() => CreateDistributor(
            Substitute.For<INotificationStore>(),
            Substitute.For<INotificationDefinitionManager>(),
            Substitute.For<IDistributedEventBus>(),
            options: options));

        exception.Message.ShouldContain(nameof(NotificationOptions.RecipientBatchSize));
    }

    [Fact]
    public void Inline_threshold_cannot_create_an_unbounded_normalization_window()
    {
        var options = new NotificationOptions
        {
            DirectDistributionUserThreshold = NotificationOptions.MaxDistributionBatchSize + 1
        };

        var exception = Should.Throw<InvalidOperationException>(() => CreateDistributor(
            Substitute.For<INotificationStore>(),
            Substitute.For<INotificationDefinitionManager>(),
            Substitute.For<IDistributedEventBus>(),
            options: options));

        exception.Message.ShouldContain(nameof(NotificationOptions.DirectDistributionUserThreshold));
    }

    [Fact]
    public async Task Legacy_protected_overrides_remain_active_on_the_compatibility_pipeline()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();
        var currentTenant = new TestCurrentTenant();
        definitionManager.Get("test").Returns(DefinitionWithChannels());
        definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(true);
        var distributor = new LegacyPublishingDistributor(
            store,
            definitionManager,
            eventBus,
            new DefaultNotificationRecipientEligibilityEvaluator(
                definitionManager,
                currentTenant,
                NullLogger<DefaultNotificationRecipientEligibilityEvaluator>.Instance),
            currentTenant);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" },
            users);

        distributor.PublishedUserIds.ShouldBe(users);
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
        await store.Received(2).InsertUserNotificationAsync(Arg.Any<UserNotificationInfo>());
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
        INotificationDataTypeRegistry? dataTypeRegistry = null,
        NotificationOptions? options = null)
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
                Options.Create(new NotificationDataOptions())),
            Options.Create(options ?? new NotificationOptions()));
    }

    private static bool HasTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string key,
        string expectedValue)
    {
        return GetTag(tags, key) == expectedValue;
    }

    private static string? GetTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    private static NotificationDefinition DefinitionWithChannels()
    {
        return new NotificationDefinition("test", new FixedLocalizableString("Test")).UseChannels("Test");
    }

    private sealed class LegacyPublishingDistributor : DefaultNotificationDistributor
    {
        public Guid[]? PublishedUserIds { get; private set; }

        public LegacyPublishingDistributor(
            INotificationStore store,
            INotificationDefinitionManager definitionManager,
            IDistributedEventBus eventBus,
            INotificationRecipientEligibilityEvaluator evaluator,
            ICurrentTenant currentTenant)
            : base(
                store,
                definitionManager,
                eventBus,
                evaluator,
                currentTenant,
                NullLogger<DefaultNotificationDistributor>.Instance,
                new NotificationDataTypeRegistry(
                    Microsoft.Extensions.Options.Options.Create(new NotificationDataOptions())))
        {
        }

        [Obsolete]
        protected override Task PublishNotificationDeliveryAsync(
            NotificationInfo notification,
            List<Guid> userIds,
            string[] channels)
        {
            PublishedUserIds = userIds.ToArray();
            return Task.CompletedTask;
        }
    }
}
