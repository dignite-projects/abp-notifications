using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Localization;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryPreferenceTests
{
    [Fact]
    public async Task Core_only_default_is_deterministic_immediate_delivery()
    {
        var candidates = new[]
        {
            new NotificationDeliveryPreferenceCandidate(Guid.NewGuid(), "Email"),
            new NotificationDeliveryPreferenceCandidate(Guid.NewGuid(), "SignalR")
        };

        var decisions = await new AllowAllNotificationDeliveryPreferenceEvaluator().EvaluateAsync(
            "test",
            Guid.NewGuid(),
            candidates,
            NotificationDeliveryPreferenceBehavior.RespectPreferences);

        decisions.Count.ShouldBe(2);
        decisions.ShouldAllBe(decision => decision.Intent == NotificationDeliveryIntent.Deliver);
        decisions.ShouldAllBe(decision => decision.NotBefore == null && decision.ReasonCode == null);
    }

    [Fact]
    public async Task Distributor_persists_inbox_and_carries_producer_resolved_channel_intent()
    {
        var userId = Guid.NewGuid();
        var store = Substitute.For<INotificationStore>();
        var definitions = Substitute.For<INotificationDefinitionManager>();
        definitions.Get("test").Returns(new NotificationDefinition(
            "test",
            new FixedLocalizableString("Test")).UseChannels("Email"));
        var eligibility = Substitute.For<INotificationRecipientEligibilityEvaluator>();
        eligibility.EvaluateAsync(
                "test",
                Arg.Any<IReadOnlyCollection<Guid>>(),
                null,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            .Returns(new NotificationRecipientEligibilityResult(new[] { userId }, Array.Empty<Guid>()));
        var preference = Substitute.For<INotificationDeliveryPreferenceEvaluator>();
        preference.EvaluateAsync(
                "test",
                null,
                Arg.Any<IReadOnlyCollection<NotificationDeliveryPreferenceCandidate>>(),
                NotificationDeliveryPreferenceBehavior.RespectPreferences,
                Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                NotificationDeliveryPreferenceDecision.Suppress(
                    userId,
                    "Email",
                    NotificationDeliveryPreferenceReasonCodes.UserOptOut)
            });
        var eventBus = Substitute.For<IDistributedEventBus>();
        NotificationDeliveryRequestedEto? published = null;
        eventBus.WhenForAnyArgs(bus => bus.PublishAsync(Arg.Any<NotificationDeliveryRequestedEto>()))
            .Do(call => published = call.Arg<NotificationDeliveryRequestedEto>());
        var distributor = new DefaultNotificationDistributor(
            store,
            definitions,
            eventBus,
            eligibility,
            new TestCurrentTenant(),
            NullLogger<DefaultNotificationDistributor>.Instance,
            new NotificationDataTypeRegistry(Options.Create(new NotificationDataOptions())),
            preference,
            Options.Create(new NotificationDistributionOptions()));

        await distributor.DistributeAsync(new NotificationInfo
        {
            Id = Guid.NewGuid(),
            NotificationName = "test",
            CreationTime = DateTime.UtcNow
        }, new[] { userId });

        await store.Received(1).InsertUserNotificationsAsync(
            Arg.Is<IReadOnlyCollection<UserNotificationInfo>>(rows =>
                rows.Count == 1 && rows.Single().UserId == userId),
            Arg.Any<CancellationToken>());
        published.ShouldNotBeNull().Intent.ShouldBe(NotificationDeliveryIntent.Suppress);
        published.PreferenceReasonCode.ShouldBe(NotificationDeliveryPreferenceReasonCodes.UserOptOut);
    }

    [Fact]
    public async Task Remote_processor_honors_suppression_without_invoking_notifier()
    {
        var now = DateTime.UtcNow;
        var callCount = 0;
        var notifier = new TestNotifier("Email", _ =>
        {
            callCount++;
            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        });
        var (processor, store, _) = CreateProcessor(now, notifier);
        var work = CreateWork("Email");
        work.Intent = NotificationDeliveryIntent.Suppress;
        work.PreferenceReasonCode = NotificationDeliveryPreferenceReasonCodes.UserOptOut;

        await processor.ProcessAsync(work);
        await processor.ProcessAsync(work);

        callCount.ShouldBe(0);
        (await store.GetDueWorkItemsAsync(now.AddDays(1), 10)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Remote_processor_persists_delayed_work_and_executes_it_only_after_not_before()
    {
        var now = DateTime.UtcNow;
        var currentTime = now;
        var callCount = 0;
        var notifier = new TestNotifier("Email", _ =>
        {
            callCount++;
            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        });
        var (processor, store, clock) = CreateProcessor(now, notifier);
        clock.Now.Returns(_ => currentTime);
        var work = CreateWork("Email");
        work.Intent = NotificationDeliveryIntent.Delay;
        work.DeliveryNotBefore = now.AddHours(1);
        work.PreferenceReasonCode = NotificationDeliveryPreferenceReasonCodes.QuietHours;

        await processor.ProcessAsync(work);
        callCount.ShouldBe(0);
        (await store.GetDueWorkItemsAsync(now.AddMinutes(59), 10)).ShouldBeEmpty();
        (await store.GetDueWorkItemsAsync(now.AddHours(1), 10)).Single().Intent
            .ShouldBe(NotificationDeliveryIntent.Delay);

        currentTime = now.AddHours(1);
        await processor.ProcessAsync(work);
        callCount.ShouldBe(1);
    }

    [Fact]
    public void Mandatory_definition_is_explicit_and_idempotent()
    {
        var definition = new NotificationDefinition("system", new FixedLocalizableString("System"));
        definition.DeliveryPreferenceBehavior.ShouldBe(NotificationDeliveryPreferenceBehavior.RespectPreferences);
        definition.AsMandatory().AsMandatory().ShouldBeSameAs(definition);
        definition.DeliveryPreferenceBehavior.ShouldBe(NotificationDeliveryPreferenceBehavior.Mandatory);
    }

    private static (NotificationDeliveryProcessor Processor, InMemoryNotificationDeliveryStore Store, IClock Clock)
        CreateProcessor(DateTime now, INotificationNotifier notifier)
    {
        var options = Options.Create(new NotificationDeliveryOptions
        {
            DeliveryRetryJitterFactor = 0,
            InitialDeliveryRetryDelay = TimeSpan.Zero,
            MaxDeliveryRetryDelay = TimeSpan.Zero
        });
        var store = new InMemoryNotificationDeliveryStore();
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(now);
        return (new NotificationDeliveryProcessor(
            store,
            new[] { notifier },
            new NotificationDeliveryRetryPolicy(options),
            clock,
            new TestCurrentTenant(),
            options,
            NullLogger<NotificationDeliveryProcessor>.Instance), store, clock);
    }

    private static NotificationDeliveryRequestedEto CreateWork(string channel)
    {
        var notificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        return new NotificationDeliveryRequestedEto
        {
            DeliveryId = NotificationDeliveryIdentity.CreateId(null, notificationId, userId, channel),
            IdempotencyKey = NotificationDeliveryIdentity.CreateIdempotencyKey(null, notificationId, userId, channel),
            NotificationId = notificationId,
            NotificationName = "test",
            UserId = userId,
            Channel = channel,
            CreationTime = DateTime.UtcNow
        };
    }

    private sealed class TestNotifier : INotificationNotifier
    {
        private readonly Func<NotificationDeliveryRequestedEto, Task<NotificationDeliveryResult>> _deliver;

        public string Name { get; }

        public TestNotifier(
            string name,
            Func<NotificationDeliveryRequestedEto, Task<NotificationDeliveryResult>> deliver)
        {
            Name = name;
            _deliver = deliver;
        }

        public Task<NotificationDeliveryResult> DeliverAsync(
            NotificationDeliveryRequestedEto workItem,
            CancellationToken cancellationToken = default)
        {
            return _deliver(workItem);
        }
    }
}
