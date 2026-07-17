using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
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
    private readonly INotificationDefinitionManager _definitionManager =
        Substitute.For<INotificationDefinitionManager>();
    private readonly INotificationDataTypeRegistry _dataTypeRegistry;

    public DefaultNotificationPublisherTests()
    {
        _definitionManager.Get(Arg.Any<string>()).Returns(call =>
            new NotificationDefinition(
                call.Arg<string>(),
                new FixedLocalizableString(call.Arg<string>())));

        var dataOptions = new NotificationDataOptions();
        dataOptions.Add<MessageNotificationData>();
        dataOptions.Add<LocalizableMessageNotificationData>();
        _dataTypeRegistry = new NotificationDataTypeRegistry(Options.Create(dataOptions));
    }

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
            currentTenant,
            _definitionManager,
            _dataTypeRegistry);
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
    public async Task Built_in_capabilities_prepare_once_and_enqueue_only_bounded_explicit_recipient_jobs()
    {
        var distributor = Substitute.For<INotificationDistributor, IPreparedNotificationDistributor>();
        var preparedDistributor = (IPreparedNotificationDistributor)distributor;
        preparedDistributor.SupportsPreparedDistribution.Returns(true);
        var store = Substitute.For<INotificationStore, IBatchedNotificationStore>();
        var batchedStore = (IBatchedNotificationStore)store;
        var options = Options.Create(new NotificationOptions
        {
            DirectDistributionUserThreshold = 1,
            RecipientBatchSize = 2
        });
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(DateTime.UtcNow);
        var currentTenant = Substitute.For<ICurrentTenant>();
        var publisher = new DefaultNotificationPublisher(
            options,
            distributor,
            _backgroundJobManager,
            guidGenerator,
            clock,
            currentTenant,
            _definitionManager,
            _dataTypeRegistry,
            store);
        var users = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

        await publisher.PublishAsync(
            "test",
            userIds: users,
            excludedUserIds: new[] { users[^1] });

        await batchedStore.Received(1).InsertNotificationAsync(
            Arg.Any<NotificationInfo>(),
            CancellationToken.None);
        var jobs = _backgroundJobManager.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IBackgroundJobManager.EnqueueAsync))
            .Select(call => call.GetArguments().OfType<NotificationDistributionJobArgs>().Single())
            .ToList();
        jobs.Count.ShouldBe(3);
        jobs.Select(job => job.UserIds!.Length).ShouldBe(new[] { 2, 2, 1 });
        jobs.ShouldAllBe(job =>
            job.NotificationAlreadyPersisted &&
            job.ExcludedUserIds == null &&
            job.Notification.Id == jobs[0].Notification.Id);
        jobs.SelectMany(job => job.UserIds!).ShouldBe(users.Take(5));
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

    [Fact]
    public async Task Matching_payload_and_required_entity_contract_publish_normally()
    {
        var definition = new NotificationDefinition("typed", new FixedLocalizableString("Typed"))
            .WithPayload<MessageNotificationData>()
            .WithEntityContract(NotificationEntityRequirement.Required, "Demo.Order");
        _definitionManager.Get("typed").Returns(definition);
        var publisher = CreatePublisher(threshold: 3);

        await publisher.PublishAsync(
            "typed",
            new MessageNotificationData("ok"),
            new NotificationEntityIdentifier("Demo.Order", "42"),
            userIds: new[] { Guid.NewGuid() });

        await _distributor.Received(1).DistributeAsync(
            Arg.Is<NotificationInfo>(notification =>
                notification.Data is MessageNotificationData &&
                notification.EntityTypeName == "Demo.Order" &&
                notification.EntityId == "42"),
            Arg.Any<Guid[]?>(),
            Arg.Any<Guid[]?>());
        await _backgroundJobManager.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
    }

    [Fact]
    public async Task Mismatched_payload_discriminator_fails_before_any_side_effect()
    {
        _definitionManager.Get("typed").Returns(
            new NotificationDefinition("typed", new FixedLocalizableString("Typed"))
                .WithPayload<MessageNotificationData>());
        var publisher = CreatePublisher(threshold: 3);

        var exception = await Should.ThrowAsync<AbpException>(() => publisher.PublishAsync(
            "typed",
            new LocalizableMessageNotificationData("Test", "Wrong"),
            userIds: new[] { Guid.NewGuid() }));

        exception.Message.ShouldContain("Dignite.Message");
        exception.Message.ShouldContain("Dignite.LocalizableMessage");
        await ShouldHaveNoPublishSideEffectsAsync();
    }

    [Fact]
    public async Task Missing_required_payload_fails_before_background_enqueue()
    {
        _definitionManager.Get("typed").Returns(
            new NotificationDefinition("typed", new FixedLocalizableString("Typed"))
                .WithPayload<MessageNotificationData>());
        var publisher = CreatePublisher(threshold: 0);

        var exception = await Should.ThrowAsync<AbpException>(() => publisher.PublishAsync(
            "typed",
            userIds: new[] { Guid.NewGuid() }));

        exception.Message.ShouldContain("no payload");
        await ShouldHaveNoPublishSideEffectsAsync();
    }

    [Fact]
    public async Task Required_entity_must_be_present_before_publish()
    {
        _definitionManager.Get("entity.required").Returns(
            new NotificationDefinition("entity.required", new FixedLocalizableString("Required"))
                .WithEntityContract(NotificationEntityRequirement.Required, "Demo.Order"));
        var publisher = CreatePublisher(threshold: 3);

        var exception = await Should.ThrowAsync<AbpException>(() => publisher.PublishAsync(
            "entity.required",
            userIds: new[] { Guid.NewGuid() }));

        exception.Message.ShouldContain("requires an entity identity");
        await ShouldHaveNoPublishSideEffectsAsync();
    }

    [Fact]
    public async Task Forbidden_entity_must_be_absent_before_publish()
    {
        _definitionManager.Get("entity.forbidden").Returns(
            new NotificationDefinition("entity.forbidden", new FixedLocalizableString("Forbidden"))
                .WithEntityContract(NotificationEntityRequirement.Forbidden));
        var publisher = CreatePublisher(threshold: 3);

        var exception = await Should.ThrowAsync<AbpException>(() => publisher.PublishAsync(
            "entity.forbidden",
            entityIdentifier: new NotificationEntityIdentifier("Demo.Order", "42"),
            userIds: new[] { Guid.NewGuid() }));

        exception.Message.ShouldContain("forbids an entity identity");
        await ShouldHaveNoPublishSideEffectsAsync();
    }

    [Fact]
    public async Task Optional_entity_may_be_absent()
    {
        _definitionManager.Get("entity.optional").Returns(
            new NotificationDefinition("entity.optional", new FixedLocalizableString("Optional"))
                .WithEntityContract(NotificationEntityRequirement.Optional, "Demo.Order"));
        var publisher = CreatePublisher(threshold: 3);

        await publisher.PublishAsync("entity.optional", userIds: new[] { Guid.NewGuid() });

        await _distributor.Received(1).DistributeAsync(
            Arg.Is<NotificationInfo>(notification =>
                notification.EntityTypeName == null && notification.EntityId == null),
            Arg.Any<Guid[]?>(),
            Arg.Any<Guid[]?>());
    }

    [Fact]
    public async Task Entity_type_name_constraint_is_ordinal_and_fails_before_publish()
    {
        _definitionManager.Get("entity.typed").Returns(
            new NotificationDefinition("entity.typed", new FixedLocalizableString("Entity typed"))
                .WithEntityContract(NotificationEntityRequirement.Optional, "Demo.Order"));
        var publisher = CreatePublisher(threshold: 3);

        var exception = await Should.ThrowAsync<AbpException>(() => publisher.PublishAsync(
            "entity.typed",
            entityIdentifier: new NotificationEntityIdentifier("demo.order", "42"),
            userIds: new[] { Guid.NewGuid() }));

        exception.Message.ShouldContain("Demo.Order");
        exception.Message.ShouldContain("demo.order");
        await ShouldHaveNoPublishSideEffectsAsync();
    }

    [Fact]
    public async Task Legacy_definition_without_contract_remains_permissive()
    {
        var publisher = CreatePublisher(threshold: 3);

        await publisher.PublishAsync(
            "legacy",
            new LocalizableMessageNotificationData("Test", "AnyPayload"),
            new NotificationEntityIdentifier("Any.Entity", "42"),
            userIds: new[] { Guid.NewGuid() });

        await _distributor.Received(1).DistributeAsync(
            Arg.Is<NotificationInfo>(notification =>
                notification.Data is LocalizableMessageNotificationData &&
                notification.EntityTypeName == "Any.Entity"),
            Arg.Any<Guid[]?>(),
            Arg.Any<Guid[]?>());
    }

    [Fact]
    public async Task Trusted_recipient_bypass_does_not_bypass_definition_contracts()
    {
        _definitionManager.Get("typed").Returns(
            new NotificationDefinition("typed", new FixedLocalizableString("Typed"))
                .WithPayload<MessageNotificationData>());
        var publisher = CreatePublisher(threshold: 3);

        await Should.ThrowAsync<AbpException>(() =>
            publisher.PublishToExplicitRecipientsWithoutEligibilityChecksAsync(
                "typed",
                new[] { Guid.NewGuid() }));

        await ShouldHaveNoPublishSideEffectsAsync();
    }

    private async Task ShouldHaveNoPublishSideEffectsAsync()
    {
        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
        await _distributor.DidNotReceiveWithAnyArgs()
            .DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(default!, default!, default);
        await _backgroundJobManager.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
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
                new DefaultNotificationDistributor(
                    Store,
                    definitionManager,
                    EventBus,
                    new DefaultNotificationRecipientEligibilityEvaluator(
                        definitionManager,
                        CurrentTenant,
                        NullLogger<DefaultNotificationRecipientEligibilityEvaluator>.Instance),
                    CurrentTenant,
                    NullLogger<DefaultNotificationDistributor>.Instance,
                    new NotificationDataTypeRegistry(Options.Create(new NotificationDataOptions()))),
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
    public async Task Distribution_job_forwards_cancellation_to_a_capable_distributor()
    {
        var distributor = Substitute.For<INotificationDistributor, ICancellableNotificationDistributor>();
        var cancellableDistributor = (ICancellableNotificationDistributor)distributor;
        var currentTenant = new TestCurrentTenant();
        using var cancellation = new CancellationTokenSource();
        var notification = new NotificationInfo
        {
            Id = Guid.NewGuid(),
            NotificationName = "test",
            TenantId = Guid.NewGuid()
        };
        var userIds = new[] { Guid.NewGuid() };
        var job = new NotificationDistributionJob(distributor, currentTenant);

        await job.ExecuteAsync(
            new NotificationDistributionJobArgs(notification, userIds, null),
            cancellation.Token);

        await cancellableDistributor.Received(1).DistributeAsync(
            notification,
            userIds,
            null,
            cancellation.Token);
        await distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
        currentTenant.Id.ShouldBeNull();
    }

    [Fact]
    public async Task Distribution_job_routes_a_prepared_batch_without_reinserting_the_notification()
    {
        var distributor = Substitute.For<INotificationDistributor, IPreparedNotificationDistributor>();
        var preparedDistributor = (IPreparedNotificationDistributor)distributor;
        preparedDistributor.SupportsPreparedDistribution.Returns(true);
        var currentTenant = new TestCurrentTenant();
        var notification = new NotificationInfo
        {
            Id = Guid.NewGuid(),
            NotificationName = "test",
            TenantId = Guid.NewGuid()
        };
        var userIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var job = new NotificationDistributionJob(distributor, currentTenant);

        await job.ExecuteAsync(new NotificationDistributionJobArgs(
            notification,
            userIds,
            excludedUserIds: null,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements,
            notificationAlreadyPersisted: true));

        await preparedDistributor.Received(1).DistributePreparedAsync(
            notification,
            userIds,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements,
            Arg.Any<CancellationToken>());
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
