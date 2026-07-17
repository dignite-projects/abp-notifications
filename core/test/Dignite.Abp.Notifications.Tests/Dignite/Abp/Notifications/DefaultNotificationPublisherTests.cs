using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationPublisherTests
{
    private readonly INotificationDistributor _distributor = Substitute.For<INotificationDistributor>();
    private readonly IBackgroundJobManager _backgroundJobManager = Substitute.For<IBackgroundJobManager>();

    private DefaultNotificationPublisher CreatePublisher(int threshold, Guid? tenantId = null)
    {
        var options = Options.Create(new NotificationOptions
        {
            DirectDistributionUserThreshold = threshold
        });

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        var clock = Substitute.For<IClock>();
        clock.Now.Returns(DateTime.UtcNow);

        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(tenantId);

        return new DefaultNotificationPublisher(
            options,
            _distributor,
            _backgroundJobManager,
            guidGenerator,
            clock,
            currentTenant);
    }

    [Fact]
    public async Task Distributes_inline_when_at_or_below_threshold()
    {
        var tenantId = Guid.NewGuid();
        var publisher = CreatePublisher(threshold: 3, tenantId);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishAsync("test", userIds: users);

        await _distributor.Received(1).DistributeAsync(
            Arg.Is<NotificationInfo>(n => n.TenantId == tenantId),
            Arg.Is<Guid[]>(u => u.Length == 3),
            Arg.Any<Guid[]?>());
        await _backgroundJobManager.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
    }

    [Fact]
    public async Task Named_bypass_uses_the_explicit_recipient_bypass_inline()
    {
        var publisher = CreatePublisher(threshold: 3);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishToExplicitRecipientsWithoutEligibilityChecksAsync("test", users);

        await _distributor.Received(1).DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
            Arg.Any<NotificationInfo>(),
            Arg.Is<Guid[]>(actual => actual.SequenceEqual(users)),
            null);
        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
    }

    [Fact]
    public async Task Named_bypass_requires_an_explicit_recipient_array()
    {
        var publisher = CreatePublisher(threshold: 3);

        await Should.ThrowAsync<ArgumentNullException>(() =>
            publisher.PublishToExplicitRecipientsWithoutEligibilityChecksAsync("test", null!));

        await _distributor.DidNotReceiveWithAnyArgs()
            .DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(default!, default!, default);
    }

    [Fact]
    public async Task Empty_explicit_recipient_list_is_a_no_op()
    {
        var publisher = CreatePublisher(threshold: 3);

        await publisher.PublishAsync("test", userIds: Array.Empty<Guid>());

        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
        await _backgroundJobManager.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
    }

    [Fact]
    public async Task Duplicate_recipients_are_normalized_before_the_inline_threshold_is_evaluated()
    {
        var publisher = CreatePublisher(threshold: 2);
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        await publisher.PublishAsync("test", userIds: new[] { u1, u1, u2 });

        await _distributor.Received(1).DistributeAsync(
            Arg.Any<NotificationInfo>(),
            Arg.Is<Guid[]>(users => users.Length == 2 && users[0] == u1 && users[1] == u2),
            Arg.Any<Guid[]?>());
        await _backgroundJobManager.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
    }

    [Fact]
    public async Task Publishes_the_caller_supplied_entity_type_name_verbatim()
    {
        var publisher = CreatePublisher(threshold: 3);

        await publisher.PublishAsync(
            "test",
            entityIdentifier: new NotificationEntityIdentifier("Demo.Order", "1001"),
            userIds: new[] { Guid.NewGuid() });

        // EntityTypeName is persisted, matched by string equality against stored subscriptions, returned over REST
        // and used as the EntityLinkResolvers key. It must be the caller's stable string, never a CLR type name —
        // notifications-invariants.md §1.
        await _distributor.Received(1).DistributeAsync(
            Arg.Is<NotificationInfo>(n => n.EntityTypeName == "Demo.Order" && n.EntityId == "1001"),
            Arg.Any<Guid[]?>(),
            Arg.Any<Guid[]?>());
    }

    [Fact]
    public async Task Enqueues_background_job_when_above_threshold()
    {
        var tenantId = Guid.NewGuid();
        var publisher = CreatePublisher(threshold: 2, tenantId);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishAsync("test", userIds: users);

        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<NotificationDistributionJobArgs>(args => args.Notification.TenantId == tenantId));
        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
    }

    [Fact]
    public async Task Named_bypass_is_preserved_in_background_job_args()
    {
        var publisher = CreatePublisher(threshold: 1);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishToExplicitRecipientsWithoutEligibilityChecksAsync("test", users);

        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<NotificationDistributionJobArgs>(args =>
                args.RecipientEligibilityMode ==
                NotificationRecipientEligibilityMode.BypassDefinitionRequirements
                && args.UserIds != null
                && args.UserIds.SequenceEqual(users)));
    }

    [Fact]
    public async Task Duplicate_recipients_are_normalized_before_the_background_threshold_is_evaluated()
    {
        var publisher = CreatePublisher(threshold: 1);
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        await publisher.PublishAsync("test", userIds: new[] { u1, u1, u2, u2 });

        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<NotificationDistributionJobArgs>(args =>
                args.UserIds != null
                && args.UserIds.Length == 2
                && args.UserIds[0] == u1
                && args.UserIds[1] == u2));
        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
    }

    [Fact]
    public async Task Null_recipient_list_is_preserved_for_subscription_distribution()
    {
        var publisher = CreatePublisher(threshold: 3);

        await publisher.PublishAsync("test", userIds: null);

        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<NotificationDistributionJobArgs>(args => args.UserIds == null));
        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
    }

    /// <summary>
    /// The background worker is the only context that has to restore a tenant: ABP's BackgroundJobExecuter only does
    /// it for IMultiTenant job args, and NotificationDistributionJobArgs is not one. So these tests run the job over a
    /// REAL DefaultNotificationDistributor, starting from no ambient tenant, and assert on what the pipeline actually
    /// observed — substituting the distributor would only prove that Change() was called, not that it mattered.
    /// </summary>
    private sealed class JobPipeline
    {
        public TestCurrentTenant CurrentTenant { get; } = new();
        public INotificationStore Store { get; } = Substitute.For<INotificationStore>();
        public IDistributedEventBus EventBus { get; } = Substitute.For<IDistributedEventBus>();

        public bool StoreWasCalled { get; private set; }
        public Guid? TenantSeenByStore { get; private set; }
        public Guid? TenantSeenByPublish { get; private set; }
        public NotificationDeliveryEto? Published { get; private set; }

        public NotificationDistributionJob CreateJob()
        {
            var definitionManager = Substitute.For<INotificationDefinitionManager>();
            definitionManager.Get("test")
                .Returns(new NotificationDefinition("test", new FixedLocalizableString("Test")).UseChannels("Test"));

            var subscribedUser = Guid.NewGuid();
            Store.GetSubscriptionsAsync("test", null, null).Returns(new List<NotificationSubscriptionInfo>
            {
                new() { UserId = subscribedUser, NotificationName = "test" }
            });
            definitionManager.IsAvailableAsync("test", subscribedUser).Returns(true);
            definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(true);

            Store.When(x => x.InsertNotificationAsync(Arg.Any<NotificationInfo>()))
                .Do(_ =>
                {
                    StoreWasCalled = true;
                    TenantSeenByStore = CurrentTenant.Id;
                });
            EventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
                .Do(ci =>
                {
                    TenantSeenByPublish = CurrentTenant.Id;
                    Published = ci.Arg<NotificationDeliveryEto>();
                });

            return new NotificationDistributionJob(
                new DefaultNotificationDistributor(Store, definitionManager, EventBus),
                CurrentTenant);
        }

        public Task ExecuteAsync(Guid? notificationTenantId)
        {
            return ExecuteAsync(notificationTenantId, new[] { Guid.NewGuid() });
        }

        public Task ExecuteAsync(Guid? notificationTenantId, Guid[]? userIds)
        {
            return CreateJob().ExecuteAsync(new NotificationDistributionJobArgs(
                new NotificationInfo
                {
                    Id = Guid.NewGuid(),
                    NotificationName = "test",
                    TenantId = notificationTenantId
                },
                userIds,
                null));
        }
    }

    [Fact]
    public async Task Distribution_job_restores_the_notification_tenant_on_the_background_worker()
    {
        var tenantId = Guid.NewGuid();
        var pipeline = new JobPipeline();

        // A worker thread picked the job off the queue: no ambient tenant.
        pipeline.CurrentTenant.Id.ShouldBeNull();

        await pipeline.ExecuteAsync(tenantId);

        pipeline.TenantSeenByStore.ShouldBe(tenantId);
        pipeline.TenantSeenByPublish.ShouldBe(tenantId);
        pipeline.Published.ShouldNotBeNull();
        pipeline.Published!.TenantId.ShouldBe(tenantId);
        pipeline.CurrentTenant.Id.ShouldBeNull();
    }

    [Fact]
    public async Task Distribution_job_stays_on_the_host_when_the_notification_has_no_tenant()
    {
        var pipeline = new JobPipeline();

        await pipeline.ExecuteAsync(notificationTenantId: null);

        pipeline.StoreWasCalled.ShouldBeTrue();
        pipeline.TenantSeenByStore.ShouldBeNull();
        pipeline.TenantSeenByPublish.ShouldBeNull();
        pipeline.Published.ShouldNotBeNull();
        pipeline.Published!.TenantId.ShouldBeNull();
    }

    [Fact]
    public async Task Distribution_job_preserves_an_empty_explicit_list_as_a_no_op()
    {
        var pipeline = new JobPipeline();

        await pipeline.ExecuteAsync(Guid.NewGuid(), Array.Empty<Guid>());

        pipeline.StoreWasCalled.ShouldBeFalse();
        pipeline.Published.ShouldBeNull();
        await pipeline.Store.DidNotReceiveWithAnyArgs().GetSubscriptionsAsync(default!, default, default);
    }

    [Fact]
    public async Task Distribution_job_invokes_the_bypass_only_for_explicit_recipients_in_the_notification_tenant()
    {
        var distributor = Substitute.For<INotificationDistributor>();
        var currentTenant = new TestCurrentTenant();
        var tenantId = Guid.NewGuid();
        var userIds = new[] { Guid.NewGuid() };
        Guid? tenantSeen = null;
        distributor.When(service => service.DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
                Arg.Any<NotificationInfo>(),
                Arg.Any<Guid[]>(),
                Arg.Any<Guid[]?>()))
            .Do(_ => tenantSeen = currentTenant.Id);
        var job = new NotificationDistributionJob(distributor, currentTenant);

        await job.ExecuteAsync(new NotificationDistributionJobArgs(
            new NotificationInfo
            {
                Id = Guid.NewGuid(),
                NotificationName = "test",
                TenantId = tenantId
            },
            userIds,
            null,
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements));

        tenantSeen.ShouldBe(tenantId);
        await distributor.Received(1).DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
            Arg.Any<NotificationInfo>(), userIds, null);
        await distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
        currentTenant.Id.ShouldBeNull();
    }

    [Fact]
    public async Task Distribution_job_rejects_a_subscription_bypass()
    {
        var job = new NotificationDistributionJob(
            Substitute.For<INotificationDistributor>(),
            new TestCurrentTenant());

        await Should.ThrowAsync<ArgumentException>(() => job.ExecuteAsync(new NotificationDistributionJobArgs(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" },
            null,
            null,
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements)));
    }
}
