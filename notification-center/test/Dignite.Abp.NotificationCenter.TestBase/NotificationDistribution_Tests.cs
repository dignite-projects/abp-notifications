using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using NSubstitute;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Provider-agnostic recipient-semantics scenarios. Running the same direct and background distribution
/// paths against both stores prevents EF Core and MongoDB from drifting on persistence or deduplication.
/// </summary>
public abstract class NotificationDistribution_Tests<TStartupModule> : NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static NotificationInfo NewNotification(Guid id)
    {
        return new NotificationInfo
        {
            Id = id,
            NotificationName = "order.shipped",
            Data = new MessageNotificationData("hi"),
            Severity = NotificationSeverity.Info,
            CreationTime = DateTime.UtcNow
        };
    }

    private DefaultNotificationDistributor CreateDistributor(IDistributedEventBus eventBus)
    {
        return new DefaultNotificationDistributor(
            GetRequiredService<INotificationStore>(),
            GetRequiredService<INotificationDefinitionManager>(),
            eventBus);
    }

    private Task DistributeAsync(
        bool background,
        DefaultNotificationDistributor distributor,
        NotificationInfo notification,
        Guid[]? userIds)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            if (background)
            {
                var job = new NotificationDistributionJob(
                    distributor,
                    GetRequiredService<ICurrentTenant>());
                await job.ExecuteAsync(new NotificationDistributionJobArgs(notification, userIds, null));
            }
            else
            {
                await distributor.DistributeAsync(notification, userIds);
            }
        });
    }

    private async Task InsertSubscriptionAsync(Guid userId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<INotificationStore>().InsertSubscriptionAsync(
                new NotificationSubscriptionInfo
                {
                    UserId = userId,
                    NotificationName = "order.shipped"
                });
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_recipient_list_resolves_subscriptions_on_direct_and_background_paths(bool background)
    {
        var subscriber = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await InsertSubscriptionAsync(subscriber);

        var eventBus = Substitute.For<IDistributedEventBus>();
        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(call => published = call.Arg<NotificationDeliveryEto>());

        await DistributeAsync(background, CreateDistributor(eventBus), NewNotification(notificationId), null);

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<INotificationStore>()
                .GetUserNotificationCountAsync(subscriber)).ShouldBe(1);
        });
        published.ShouldNotBeNull();
        published!.UserIds.ShouldBe(new[] { subscriber });
        await eventBus.Received(1).PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_explicit_list_is_a_no_op_on_direct_and_background_paths(bool background)
    {
        var subscriber = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await InsertSubscriptionAsync(subscriber);

        var eventBus = Substitute.For<IDistributedEventBus>();
        await DistributeAsync(
            background,
            CreateDistributor(eventBus),
            NewNotification(notificationId),
            Array.Empty<Guid>());

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IRepository<Notification, Guid>>()
                .FindAsync(notificationId)).ShouldBeNull();
            (await GetRequiredService<INotificationStore>()
                .GetUserNotificationCountAsync(subscriber)).ShouldBe(0);
        });
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_explicit_users_produce_one_inbox_row_and_delivery_per_user(bool background)
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var eventBus = Substitute.For<IDistributedEventBus>();
        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(call => published = call.Arg<NotificationDeliveryEto>());

        await DistributeAsync(
            background,
            CreateDistributor(eventBus),
            NewNotification(notificationId),
            new[] { u1, u1, u2, u2 });

        await WithUnitOfWorkAsync(async () =>
        {
            var store = GetRequiredService<INotificationStore>();
            (await store.GetUserNotificationCountAsync(u1)).ShouldBe(1);
            (await store.GetUserNotificationCountAsync(u2)).ShouldBe(1);
        });
        published.ShouldNotBeNull();
        published!.UserIds.ShouldBe(new[] { u1, u2 });
        published.UserIds.Distinct().Count().ShouldBe(2);
        await eventBus.Received(1).PublishAsync(Arg.Any<NotificationDeliveryEto>());
    }
}
