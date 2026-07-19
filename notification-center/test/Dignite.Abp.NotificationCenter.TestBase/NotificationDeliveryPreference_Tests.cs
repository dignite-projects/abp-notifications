using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Provider-parity scenarios for persistent user delivery preferences.</summary>
public abstract class NotificationDeliveryPreference_Tests<TStartupModule> : NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task Precedence_is_exact_then_notification_then_channel_then_global_then_default_allow()
    {
        var userId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var manager = GetRequiredService<INotificationDeliveryPreferenceManager>();
            await manager.SetAsync(userId, null, null, false);
            await manager.SetAsync(userId, null, "Email", true);
            await manager.SetAsync(userId, "order.shipped", null, false);
            await manager.SetAsync(userId, "order.shipped", "Email", true);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var decisions = await GetRequiredService<INotificationDeliveryPreferenceEvaluator>().EvaluateAsync(
                "order.shipped",
                null,
                new[]
                {
                    new NotificationDeliveryPreferenceCandidate(userId, "Email"),
                    new NotificationDeliveryPreferenceCandidate(userId, "SignalR")
                },
                NotificationDeliveryPreferenceBehavior.RespectPreferences);
            decisions.Single(decision => decision.Channel == "EMAIL").Intent
                .ShouldBe(NotificationDeliveryIntent.Deliver);
            decisions.Single(decision => decision.Channel == "SIGNALR").Intent
                .ShouldBe(NotificationDeliveryIntent.Suppress);

            var other = await GetRequiredService<INotificationDeliveryPreferenceEvaluator>().EvaluateAsync(
                "other.notification",
                null,
                new[]
                {
                    new NotificationDeliveryPreferenceCandidate(userId, "Email"),
                    new NotificationDeliveryPreferenceCandidate(userId, "SignalR")
                },
                NotificationDeliveryPreferenceBehavior.RespectPreferences);
            other.Single(decision => decision.Channel == "EMAIL").Intent
                .ShouldBe(NotificationDeliveryIntent.Deliver);
            other.Single(decision => decision.Channel == "SIGNALR").Intent
                .ShouldBe(NotificationDeliveryIntent.Suppress);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var defaultUser = Guid.NewGuid();
            var decision = (await GetRequiredService<INotificationDeliveryPreferenceEvaluator>().EvaluateAsync(
                "other.notification",
                null,
                new[] { new NotificationDeliveryPreferenceCandidate(defaultUser, "Email") },
                NotificationDeliveryPreferenceBehavior.RespectPreferences)).Single();
            decision.Intent.ShouldBe(NotificationDeliveryIntent.Deliver);
        });
    }

    [Fact]
    public async Task Explicit_and_subscription_recipients_use_the_same_channel_rule_without_losing_inbox_or_other_channel()
    {
        var explicitUser = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var manager = GetRequiredService<INotificationDeliveryPreferenceManager>();
            await manager.SetAsync(explicitUser, null, "Email", false);
            await manager.SetAsync(subscriber, null, "Email", false);
            await GetRequiredService<INotificationStore>().InsertSubscriptionAsync(new NotificationSubscriptionInfo
            {
                UserId = subscriber,
                NotificationName = TestNotificationDefinitionProvider.PreferenceNotification,
                CreationTime = DateTime.UtcNow
            });
        });

        var eventBus = Substitute.For<IDistributedEventBus>();
        var published = new List<NotificationDeliveryRequestedEto>();
        eventBus.WhenForAnyArgs(bus => bus.PublishAsync(Arg.Any<NotificationDeliveryRequestedEto>()))
            .Do(call => published.Add(call.Arg<NotificationDeliveryRequestedEto>()));
        var distributor = CreateDistributor(eventBus);
        var explicitNotification = NewPreferenceNotification(Guid.NewGuid());
        var subscriptionNotification = NewPreferenceNotification(Guid.NewGuid());

        await WithUnitOfWorkAsync(() => distributor.DistributeAsync(
            explicitNotification,
            new[] { explicitUser }));
        await WithUnitOfWorkAsync(() => distributor.DistributeAsync(subscriptionNotification));

        published.Count.ShouldBe(4);
        foreach (var userId in new[] { explicitUser, subscriber })
        {
            published.Single(item => item.UserId == userId && item.Channel == "Email").Intent
                .ShouldBe(NotificationDeliveryIntent.Suppress);
            published.Single(item => item.UserId == userId && item.Channel == "SignalR").Intent
                .ShouldBe(NotificationDeliveryIntent.Deliver);
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var rows = await GetRequiredService<IRepository<UserNotification, Guid>>().GetListAsync(row =>
                row.NotificationId == explicitNotification.Id || row.NotificationId == subscriptionNotification.Id);
            rows.Select(row => row.UserId).ShouldBe(new[] { explicitUser, subscriber }, ignoreOrder: true);
        });
    }

    [Fact]
    public async Task Quiet_hours_delay_normal_work_while_mandatory_work_is_immediate()
    {
        var userId = Guid.NewGuid();
        var clock = GetRequiredService<IClock>();
        var now = clock.Now.ToUniversalTime();
        var minute = now.Hour * 60 + now.Minute;
        await WithUnitOfWorkAsync(() => GetRequiredService<INotificationDeliveryPreferenceManager>()
            .SetQuietHoursAsync(userId, (minute + 1439) % 1440, (minute + 10) % 1440, "UTC"));

        await WithUnitOfWorkAsync(async () =>
        {
            var evaluator = GetRequiredService<INotificationDeliveryPreferenceEvaluator>();
            var candidates = new[] { new NotificationDeliveryPreferenceCandidate(userId, "Email") };
            var normal = (await evaluator.EvaluateAsync(
                "order.shipped",
                null,
                candidates,
                NotificationDeliveryPreferenceBehavior.RespectPreferences)).Single();
            normal.Intent.ShouldBe(NotificationDeliveryIntent.Delay);
            normal.ReasonCode.ShouldBe(NotificationDeliveryPreferenceReasonCodes.QuietHours);
            normal.NotBefore.ShouldNotBeNull().ShouldBeGreaterThan(now);

            var mandatory = (await evaluator.EvaluateAsync(
                "mandatory.test",
                null,
                candidates,
                NotificationDeliveryPreferenceBehavior.Mandatory)).Single();
            mandatory.Intent.ShouldBe(NotificationDeliveryIntent.Deliver);
            mandatory.NotBefore.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Preferences_and_quiet_hours_are_tenant_isolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        using (currentTenant.Change(tenantA))
        {
            await WithUnitOfWorkAsync(() => GetRequiredService<INotificationDeliveryPreferenceManager>()
                .SetAsync(userId, null, "Email", false));
        }

        using (currentTenant.Change(tenantA))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var decision = await EvaluateOneAsync(tenantA, userId);
                decision.Intent.ShouldBe(NotificationDeliveryIntent.Suppress);
            });
        }

        using (currentTenant.Change(tenantB))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var decision = await EvaluateOneAsync(tenantB, userId);
                decision.Intent.ShouldBe(NotificationDeliveryIntent.Deliver);
            });
        }
    }

    [Fact]
    public async Task Current_user_API_round_trips_rules_and_quiet_hours_without_cross_user_access()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        using (ChangeCurrentUser(firstUser))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var appService = GetRequiredService<INotificationDeliveryPreferenceAppService>();
                await appService.SetAsync(new SetNotificationDeliveryPreferenceDto
                {
                    Channel = "Email",
                    IsEnabled = false
                });
                await appService.SetQuietHoursAsync(new SetNotificationQuietHoursDto
                {
                    StartMinute = 1320,
                    EndMinute = 420,
                    TimeZoneId = "UTC"
                });
            });

            await WithUnitOfWorkAsync(async () =>
            {
                var appService = GetRequiredService<INotificationDeliveryPreferenceAppService>();
                var rule = (await appService.GetListAsync()).Items.Single();
                rule.Channel.ShouldBe("EMAIL");
                rule.IsEnabled.ShouldBeFalse();
                var quietHours = await appService.GetQuietHoursAsync();
                quietHours.ShouldNotBeNull().StartMinute.ShouldBe(1320);
            });
        }

        using (ChangeCurrentUser(secondUser))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var appService = GetRequiredService<INotificationDeliveryPreferenceAppService>();
                (await appService.GetListAsync()).Items.ShouldBeEmpty();
                (await appService.GetQuietHoursAsync()).ShouldBeNull();
            });
        }
    }

    [Fact]
    public async Task A_rule_whose_notification_definition_was_removed_can_still_be_deleted()
    {
        var userId = Guid.NewGuid();
        // Simulates a rule created while its definition still existed and orphaned by a later rename/removal:
        // the manager never consults the definition catalog, so this row is exactly what an upgrade leaves behind.
        await WithUnitOfWorkAsync(() => GetRequiredService<INotificationDeliveryPreferenceManager>()
            .SetAsync(userId, "removed.notification", "Email", false));

        using (ChangeCurrentUser(userId))
        {
            await WithUnitOfWorkAsync(() => GetRequiredService<INotificationDeliveryPreferenceAppService>()
                .DeleteAsync(new DeleteNotificationDeliveryPreferenceDto
                {
                    NotificationName = "removed.notification",
                    Channel = "Email"
                }));

            await WithUnitOfWorkAsync(async () =>
                (await GetRequiredService<INotificationDeliveryPreferenceAppService>().GetListAsync())
                    .Items.ShouldBeEmpty());
        }
    }

    private async Task<NotificationDeliveryPreferenceDecision> EvaluateOneAsync(Guid tenantId, Guid userId)
    {
        return (await GetRequiredService<INotificationDeliveryPreferenceEvaluator>().EvaluateAsync(
            "order.shipped",
            tenantId,
            new[] { new NotificationDeliveryPreferenceCandidate(userId, "Email") },
            NotificationDeliveryPreferenceBehavior.RespectPreferences)).Single();
    }

    private DefaultNotificationDistributor CreateDistributor(IDistributedEventBus eventBus)
    {
        return new DefaultNotificationDistributor(
            GetRequiredService<INotificationStore>(),
            GetRequiredService<INotificationDefinitionManager>(),
            eventBus,
            GetRequiredService<INotificationRecipientEligibilityEvaluator>(),
            GetRequiredService<ICurrentTenant>(),
            GetRequiredService<ILogger<DefaultNotificationDistributor>>(),
            GetRequiredService<INotificationDataTypeRegistry>(),
            GetRequiredService<INotificationDeliveryPreferenceEvaluator>(),
            Options.Create(new NotificationDistributionOptions()));
    }

    private static NotificationInfo NewPreferenceNotification(Guid id)
    {
        return new NotificationInfo
        {
            Id = id,
            NotificationName = TestNotificationDefinitionProvider.PreferenceNotification,
            Data = new MessageNotificationData("preference"),
            Severity = NotificationSeverity.Info,
            CreationTime = DateTime.UtcNow
        };
    }
}
