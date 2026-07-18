using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Guids;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceBroadcastTests
{
    [Fact]
    public void Tenant_broadcast_rejects_empty_tenant_id()
    {
        Should.Throw<ArgumentException>(() =>
            new NotificationAudienceTenantBroadcastRequest(
                Guid.Empty,
                TestNotificationDefinitionProvider.Plain));
    }

    [Fact]
    public async Task Tenant_broadcast_requires_matching_ambient_tenant()
    {
        var ambientTenantId = Guid.NewGuid();
        var requestedTenantId = Guid.NewGuid();
        var currentTenant = new TestCurrentTenant();
        using (currentTenant.Change(ambientTenantId, "tenant"))
        {
            var broadcaster = CreateBroadcaster(currentTenant: currentTenant);

            await Should.ThrowAsync<AbpException>(() => broadcaster.EnqueueTenantBroadcastAsync(
                new NotificationAudienceTenantBroadcastRequest(
                    requestedTenantId,
                    TestNotificationDefinitionProvider.Plain)));
        }
    }

    [Fact]
    public async Task Host_broadcast_enqueues_one_isolated_tenant_job_and_records_failures()
    {
        var currentTenant = new TestCurrentTenant();
        var goodTenantId = Guid.NewGuid();
        var failingTenantId = Guid.NewGuid();
        var backgroundJobManager = new FakeBackgroundJobManager();
        var unitOfWorks = new List<IUnitOfWork>();
        var unitOfWorkManager = CreateUnitOfWorkManager(unitOfWorks);
        var store = Substitute.For<INotificationStore, IBatchedNotificationStore>();
        var batchedStore = (IBatchedNotificationStore)store;
        batchedStore.InsertNotificationAsync(
                Arg.Is<NotificationInfo>(notification => notification.TenantId == failingTenantId),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("tenant enqueue failed"));
        var broadcaster = CreateBroadcaster(
            currentTenant,
            backgroundJobManager,
            store: (INotificationStore)store,
            unitOfWorkManager: unitOfWorkManager);

        var result = await broadcaster.EnqueueHostBroadcastAsync(
            new NotificationAudienceHostBroadcastRequest(
                new[] { goodTenantId, failingTenantId },
                TestNotificationDefinitionProvider.Plain));

        result.Tenants.Count.ShouldBe(2);
        result.Tenants.Single(tenant => tenant.TenantId == goodTenantId).IsEnqueued.ShouldBeTrue();
        var failed = result.Tenants.Single(tenant => tenant.TenantId == failingTenantId);
        failed.IsEnqueued.ShouldBeFalse();
        failed.NotificationId.ShouldBe(Guid.Empty);
        failed.ErrorMessage.ShouldNotBeNull();
        failed.ErrorMessage.ShouldContain("tenant enqueue failed");

        backgroundJobManager.EnqueuedArgs.ShouldHaveSingleItem()
            .ShouldBeOfType<NotificationAudienceBroadcastJobArgs>()
            .TenantId.ShouldBe(goodTenantId);
        unitOfWorks.Count.ShouldBe(2);
        await unitOfWorks[0].Received(1).CompleteAsync(Arg.Any<CancellationToken>());
        await unitOfWorks[1].DidNotReceive().CompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Job_processes_one_bounded_page_and_enqueues_resume_cursor()
    {
        var tenantId = Guid.NewGuid();
        var users = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var source = new TestAudienceRecipientSource(tenantId, users);
        var distributor = new RecordingPreparedNotificationDistributor();
        var backgroundJobManager = new FakeBackgroundJobManager();
        var progressStore = new InMemoryNotificationAudienceBroadcastProgressStore();
        var job = CreateJob(
            source,
            distributor,
            backgroundJobManager,
            recipientBatchSize: 2,
            progressStore);
        var args = NewJobArgs(tenantId);

        await job.ExecuteAsync(args, CancellationToken.None);

        source.Requests.ShouldHaveSingleItem().MaxResultCount.ShouldBe(2);
        distributor.Calls.ShouldHaveSingleItem().UserIds.ShouldBe(users.Take(2));
        var nextArgs = backgroundJobManager.EnqueuedArgs.ShouldHaveSingleItem()
            .ShouldBeOfType<NotificationAudienceBroadcastJobArgs>();
        nextArgs.TenantId.ShouldBe(tenantId);
        nextArgs.Notification.Id.ShouldBe(args.Notification.Id);
        nextArgs.Cursor.ShouldBe("2");
        nextArgs.PageIndex.ShouldBe(1);
        var progress = await progressStore.GetAsync(args.Notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Running);
        progress.CompletedPageCount.ShouldBe(1);
        progress.CandidateCount.ShouldBe(2);
        progress.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task Job_honors_cancellation_before_loading_recipients()
    {
        var tenantId = Guid.NewGuid();
        var source = new TestAudienceRecipientSource(tenantId, Guid.NewGuid());
        var distributor = new RecordingPreparedNotificationDistributor();
        var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var job = CreateJob(source, distributor, new FakeBackgroundJobManager(), recipientBatchSize: 2);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            job.ExecuteAsync(NewJobArgs(tenantId), cancellation.Token));

        source.Requests.ShouldBeEmpty();
        distributor.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_tenant_page_completes_without_enqueuing_more_work()
    {
        var tenantId = Guid.NewGuid();
        var source = new TestAudienceRecipientSource(tenantId);
        var distributor = new RecordingPreparedNotificationDistributor();
        var backgroundJobManager = new FakeBackgroundJobManager();
        var progressStore = new InMemoryNotificationAudienceBroadcastProgressStore();
        var args = NewJobArgs(tenantId);

        await CreateJob(
                source,
                distributor,
                backgroundJobManager,
                recipientBatchSize: 2,
                progressStore)
            .ExecuteAsync(args, CancellationToken.None);

        source.Requests.ShouldHaveSingleItem();
        distributor.Calls.ShouldBeEmpty();
        backgroundJobManager.EnqueuedArgs.ShouldBeEmpty();
        var progress = await progressStore.GetAsync(args.Notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Completed);
        progress.CompletedPageCount.ShouldBe(1);
        progress.CandidateCount.ShouldBe(0);
        progress.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_request_is_exposed_and_prevents_job_from_loading_recipients()
    {
        var tenantId = Guid.NewGuid();
        var source = new TestAudienceRecipientSource(tenantId, Guid.NewGuid());
        var backgroundJobManager = new FakeBackgroundJobManager();
        var progressStore = new InMemoryNotificationAudienceBroadcastProgressStore();
        var broadcaster = CreateBroadcaster(
            backgroundJobManager: backgroundJobManager,
            sources: new[] { source },
            progressStore: progressStore);

        var result = await broadcaster.EnqueueTenantBroadcastAsync(
            new NotificationAudienceTenantBroadcastRequest(
                tenantId,
                TestNotificationDefinitionProvider.Plain));
        (await broadcaster.CancelTenantBroadcastAsync(result.NotificationId, tenantId)).ShouldBeTrue();
        var enqueuedArgs = backgroundJobManager.EnqueuedArgs.ShouldHaveSingleItem()
            .ShouldBeOfType<NotificationAudienceBroadcastJobArgs>();

        await CreateJob(
                source,
                new RecordingPreparedNotificationDistributor(),
                new FakeBackgroundJobManager(),
                recipientBatchSize: 2,
                progressStore)
            .ExecuteAsync(enqueuedArgs, CancellationToken.None);

        source.Requests.ShouldBeEmpty();
        var progress = await broadcaster.GetTenantBroadcastProgressAsync(result.NotificationId, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Canceled);
        progress.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Job_rejects_tenant_mismatched_notification_args()
    {
        var jobTenantId = Guid.NewGuid();
        var notificationTenantId = Guid.NewGuid();
        var job = CreateJob(
            new TestAudienceRecipientSource(jobTenantId, Guid.NewGuid()),
            new RecordingPreparedNotificationDistributor(),
            new FakeBackgroundJobManager(),
            recipientBatchSize: 2);
        var args = NewJobArgs(jobTenantId);
        args.Notification.TenantId = notificationTenantId;

        await Should.ThrowAsync<AbpException>(() => job.ExecuteAsync(args, CancellationToken.None));
    }

    private static DefaultNotificationAudienceBroadcaster CreateBroadcaster(
        ICurrentTenant? currentTenant = null,
        IBackgroundJobManager? backgroundJobManager = null,
        INotificationStore? store = null,
        INotificationDistributor? distributor = null,
        IEnumerable<INotificationAudienceRecipientSource>? sources = null,
        INotificationAudienceBroadcastProgressStore? progressStore = null,
        IUnitOfWorkManager? unitOfWorkManager = null)
    {
        currentTenant ??= new TestCurrentTenant();
        backgroundJobManager ??= new FakeBackgroundJobManager();
        store ??= Substitute.For<INotificationStore, IBatchedNotificationStore>();
        distributor ??= CreatePreparedDistributorSubstitute();
        sources ??= new[] { new TestAudienceRecipientSource(null, Guid.NewGuid()) };
        progressStore ??= new InMemoryNotificationAudienceBroadcastProgressStore();
        unitOfWorkManager ??= CreateUnitOfWorkManager();
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(DateTime.UtcNow);
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        definitionManager.Get(Arg.Any<string>()).Returns(call =>
            new NotificationDefinition(
                    call.Arg<string>(),
                    new FixedLocalizableString(call.Arg<string>()))
                .UseChannels("Test"));

        return new DefaultNotificationAudienceBroadcaster(
            Options.Create(new NotificationOptions()),
            distributor,
            backgroundJobManager,
            guidGenerator,
            clock,
            currentTenant,
            definitionManager,
            CreateDataTypeRegistry(),
            store,
            sources,
            progressStore,
            unitOfWorkManager,
            NullLogger<DefaultNotificationAudienceBroadcaster>.Instance);
    }

    private static NotificationAudienceBroadcastJob CreateJob(
        INotificationAudienceRecipientSource source,
        RecordingPreparedNotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        int recipientBatchSize,
        INotificationAudienceBroadcastProgressStore? progressStore = null)
    {
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(_ => DateTime.UtcNow);
        return new NotificationAudienceBroadcastJob(
            Options.Create(new NotificationOptions { RecipientBatchSize = recipientBatchSize }),
            new[] { source },
            distributor,
            backgroundJobManager,
            new TestCurrentTenant(),
            progressStore ?? new InMemoryNotificationAudienceBroadcastProgressStore(),
            clock,
            NullLogger<NotificationAudienceBroadcastJob>.Instance);
    }

    private static IUnitOfWorkManager CreateUnitOfWorkManager(List<IUnitOfWork>? unitOfWorks = null)
    {
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        unitOfWorkManager
            .Begin(
                Arg.Any<AbpUnitOfWorkOptions>(),
                Arg.Any<bool>())
            .Returns(_ =>
            {
                var unitOfWork = Substitute.For<IUnitOfWork>();
                unitOfWork.CompleteAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
                unitOfWorks?.Add(unitOfWork);
                return unitOfWork;
            });
        return unitOfWorkManager;
    }

    private static INotificationDistributor CreatePreparedDistributorSubstitute()
    {
        var distributor = Substitute.For<INotificationDistributor, IPreparedNotificationDistributor>();
        ((IPreparedNotificationDistributor)distributor).SupportsPreparedDistribution.Returns(true);
        return distributor;
    }

    private static INotificationDataTypeRegistry CreateDataTypeRegistry()
    {
        var options = new NotificationDataOptions();
        options.Add<MessageNotificationData>();
        return new NotificationDataTypeRegistry(Options.Create(options));
    }

    private static NotificationAudienceBroadcastJobArgs NewJobArgs(Guid? tenantId)
    {
        return new NotificationAudienceBroadcastJobArgs(
            tenantId,
            NotificationAudienceNames.AllActiveUsers,
            new NotificationInfo
            {
                Id = Guid.NewGuid(),
                NotificationName = TestNotificationDefinitionProvider.Plain,
                CreationTime = DateTime.UtcNow,
                TenantId = tenantId
            },
            cursor: null,
            pageIndex: 0,
            excludedUserIds: null);
    }

    private sealed class TestAudienceRecipientSource : INotificationAudienceRecipientSource
    {
        private readonly Guid? _tenantId;
        private readonly IReadOnlyList<Guid> _userIds;

        public string AudienceName => NotificationAudienceNames.AllActiveUsers;

        public List<NotificationAudienceRecipientPageRequest> Requests { get; } = new();

        public TestAudienceRecipientSource(Guid? tenantId, params Guid[] userIds)
        {
            _tenantId = tenantId;
            _userIds = userIds;
        }

        public Task<NotificationAudienceRecipientPage> GetRecipientsAsync(
            NotificationAudienceRecipientPageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.TenantId.ShouldBe(_tenantId);
            Requests.Add(request);
            var skip = string.IsNullOrWhiteSpace(request.Cursor)
                ? 0
                : int.Parse(request.Cursor, CultureInfo.InvariantCulture);
            var page = _userIds.Skip(skip).Take(request.MaxResultCount).ToArray();
            var nextIndex = skip + page.Length;
            return Task.FromResult(new NotificationAudienceRecipientPage(
                page,
                nextIndex < _userIds.Count ? nextIndex.ToString(CultureInfo.InvariantCulture) : null,
                nextIndex < _userIds.Count));
        }
    }

    private sealed class RecordingPreparedNotificationDistributor :
        INotificationDistributor,
        IPreparedNotificationDistributor
    {
        public bool SupportsPreparedDistribution => true;

        public List<PreparedCall> Calls { get; } = new();

        public Task DistributeAsync(
            NotificationInfo notification,
            Guid[]? userIds = null,
            Guid[]? excludedUserIds = null)
        {
            throw new NotSupportedException();
        }

        public Task DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
            NotificationInfo notification,
            Guid[] userIds,
            Guid[]? excludedUserIds = null)
        {
            throw new NotSupportedException();
        }

        public Task DistributePreparedAsync(
            NotificationInfo notification,
            Guid[] userIds,
            NotificationRecipientEligibilityMode recipientEligibilityMode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recipientEligibilityMode.ShouldBe(NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);
            Calls.Add(new PreparedCall(notification, userIds.ToArray()));
            return Task.CompletedTask;
        }
    }

    private sealed class PreparedCall
    {
        public NotificationInfo Notification { get; }

        public Guid[] UserIds { get; }

        public PreparedCall(NotificationInfo notification, Guid[] userIds)
        {
            Notification = notification;
            UserIds = userIds;
        }
    }
}
