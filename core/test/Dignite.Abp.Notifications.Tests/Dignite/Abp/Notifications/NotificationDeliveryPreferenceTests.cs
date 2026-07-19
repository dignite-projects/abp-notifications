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
        decisions.ShouldAllBe(decision => decision.ShouldDeliver);
        decisions.ShouldAllBe(decision => decision.SuppressionReasonCode == null);
    }

    [Fact]
    public async Task Distributor_persists_inbox_but_suppresses_the_opted_out_channel_event()
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
        var publishCount = 0;
        eventBus.WhenForAnyArgs(bus => bus.PublishAsync(Arg.Any<NotificationDeliveryRequestedEto>()))
            .Do(_ => publishCount++);
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
        // The user opted out of the Email channel, so no delivery event is published; the inbox row remains.
        publishCount.ShouldBe(0);
    }

    [Fact]
    public void Mandatory_definition_is_explicit_and_idempotent()
    {
        var definition = new NotificationDefinition("system", new FixedLocalizableString("System"));
        definition.DeliveryPreferenceBehavior.ShouldBe(NotificationDeliveryPreferenceBehavior.RespectPreferences);
        definition.AsMandatory().AsMandatory().ShouldBeSameAs(definition);
        definition.DeliveryPreferenceBehavior.ShouldBe(NotificationDeliveryPreferenceBehavior.Mandatory);
    }
}
