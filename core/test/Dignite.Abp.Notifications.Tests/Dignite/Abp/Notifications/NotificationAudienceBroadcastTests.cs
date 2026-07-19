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
    public void Broadcast_request_rejects_empty_scope_tenant_id()
    {
        Should.Throw<ArgumentException>(() =>
            new NotificationAudienceBroadcastRequest(
                Guid.Empty,
                TestNotificationDefinitionProvider.Plain));
    }

    [Fact]
    public async Task Enqueue_requires_matching_ambient_tenant_for_tenant_scope()
    {
        var ambientTenantId = Guid.NewGuid();
        var requestedTenantId = Guid.NewGuid();
        var currentTenant = new TestCurrentTenant();
        using (currentTenant.Change(ambientTenantId, "tenant"))
        {
            var broadcaster = CreateBroadcaster(currentTenant: currentTenant);

            await Should.ThrowAsync<AbpException>(() => broadcaster.EnqueueAsync(
                new NotificationAudienceBroadcastRequest(
                    requestedTenantId,
                    TestNotificationDefinitionProvider.Plain)));
        }
    }

    [Fact]
    public async Task Enqueue_supports_host_scope()
    {
        var backgroundJobManager = new FakeBackgroundJobManager();
        var broadcaster = CreateBroadcaster(backgroundJobManager: backgroundJobManager);

        var result = await broadcaster.EnqueueAsync(
            new NotificationAudienceBroadcastRequest(null, TestNotificationDefinitionProvider.Plain));

        result.TenantId.ShouldBeNull();
        result.IsEnqueued.ShouldBeTrue();
        backgroundJobManager.EnqueuedArgs.ShouldHaveSingleItem()
            .ShouldBeOfType<NotificationAudienceBroadcastJobArgs>()
            .TenantId.ShouldBeNull();
    }

    [Fact]
    public async Task Enqueue_supports_matching_tenant_scope()
    {
        var tenantId = Guid.NewGuid();
        var currentTenant = new TestCurrentTenant();
        var backgroundJobManager = new FakeBackgroundJobManager();
        using (currentTenant.Change(tenantId, "tenant"))
        {
            var broadcaster = CreateBroadcaster(currentTenant, backgroundJobManager);

            var result = await broadcaster.EnqueueAsync(
                new NotificationAudienceBroadcastRequest(tenantId, TestNotificationDefinitionProvider.Plain));

            result.TenantId.ShouldBe(tenantId);
            result.IsEnqueued.ShouldBeTrue();
            backgroundJobManager.EnqueuedArgs.ShouldHaveSingleItem()
                .ShouldBeOfType<NotificationAudienceBroadcastJobArgs>()
                .TenantId.ShouldBe(tenantId);
        }
    }

    [Fact]
    public async Task Enqueue_for_tenants_requires_host_scope()
    {
        var currentTenant = new TestCurrentTenant();
        using (currentTenant.Change(Guid.NewGuid(), "tenant"))
        {
            var broadcaster = CreateBroadcaster(currentTenant);

            await Should.ThrowAsync<AbpException>(() => broadcaster.EnqueueForTenantsAsync(
                new NotificationAudienceMultiTenantBroadcastRequest(
                    new[] { Guid.NewGuid() },
                    TestNotificationDefinitionProvider.Plain)));
        }
    }

    [Fact]
    public async Task Enqueue_failure_records_sanitized_diagnosable_state()
    {
        var backgroundJobManager = new FakeBackgroundJobManager
        {
            EnqueueException = new InvalidOperationException("provider secret")
        };
        var progressStore = new InMemoryNotificationAudienceBroadcastProgressStore();
        var broadcaster = CreateBroadcaster(
            backgroundJobManager: backgroundJobManager,
            progressStore: progressStore);

        await Should.ThrowAsync<InvalidOperationException>(() => broadcaster.EnqueueAsync(
            new NotificationAudienceBroadcastRequest(null, TestNotificationDefinitionProvider.Plain)));

        var args = backgroundJobManager.EnqueuedArgs.ShouldHaveSingleItem()
            .ShouldBeOfType<NotificationAudienceBroadcastJobArgs>();
        var progress = await progressStore.GetAsync(args.Notification.Id, tenantId: null);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Failed);
        progress.FailureCode.ShouldBe("enqueue-failed");
        progress.FailureMessage.ShouldBe("The audience broadcast could not be enqueued.");
        progress.FailureMessage!.ShouldNotContain("provider secret");
    }

    [Fact]
    public async Task Enqueue_for_tenants_enqueues_isolated_jobs_and_records_failures()
    {
        var currentTenant = new TestCurrentTenant();
        var goodTenantId = Guid.NewGuid();
        var failingTenantId = Guid.NewGuid();
        var backgroundJobManager = new FakeBackgroundJobManager();
        var unitOfWorks = new List<IUnitOfWork>();
        var unitOfWorkManager = CreateUnitOfWorkManager(unitOfWorks);
        var store = Substitute.For<INotificationStore>();
        store.InsertNotificationAsync(
                Arg.Is<NotificationInfo>(notification => notification.TenantId == failingTenantId),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("tenant enqueue failed"));
        var broadcaster = CreateBroadcaster(
            currentTenant,
            backgroundJobManager,
            store: store,
            unitOfWorkManager: unitOfWorkManager);

        var result = await broadcaster.EnqueueForTenantsAsync(
            new NotificationAudienceMultiTenantBroadcastRequest(
                new[] { goodTenantId, failingTenantId },
                TestNotificationDefinitionProvider.Plain));

        result.Results.Count.ShouldBe(2);
        result.Results.Single(scope => scope.TenantId == goodTenantId).IsEnqueued.ShouldBeTrue();
        var failed = result.Results.Single(scope => scope.TenantId == failingTenantId);
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
    public async Task Job_processes_one_bounded_page_and_enqueues_continuation_token()
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
        nextArgs.ContinuationToken.ShouldBe("2");
        nextArgs.PageIndex.ShouldBe(1);
        var progress = await progressStore.GetAsync(args.Notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Running);
        progress.CompletedPageCount.ShouldBe(1);
        progress.CandidateCount.ShouldBe(2);
        progress.HasMore.ShouldBeTrue();
    }

    [Fact]
    public void Recipient_page_derives_has_more_from_opaque_next_token()
    {
        const string opaqueToken = "provider-owned::token";

        var continuingPage = new NotificationAudienceRecipientPage(Array.Empty<Guid>(), opaqueToken);
        var finalPage = new NotificationAudienceRecipientPage(Array.Empty<Guid>(), null);

        continuingPage.NextContinuationToken.ShouldBe(opaqueToken);
        continuingPage.HasMore.ShouldBeTrue();
        finalPage.HasMore.ShouldBeFalse();
        Should.Throw<ArgumentException>(() =>
            new NotificationAudienceRecipientPage(Array.Empty<Guid>(), " "));

        var progress = new NotificationAudienceBroadcastProgress { NextContinuationToken = opaqueToken };
        progress.HasMore.ShouldBeTrue();
        progress.NextContinuationToken = null;
        progress.HasMore.ShouldBeFalse();
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
    public async Task Cancellation_at_page_boundary_prevents_scheduling_the_next_page()
    {
        var tenantId = Guid.NewGuid();
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var source = new TestAudienceRecipientSource(tenantId, users);
        var distributor = new RecordingPreparedNotificationDistributor();
        var backgroundJobManager = new FakeBackgroundJobManager();
        var progressStore = new CancelAfterPageProgressStore();
        var args = NewJobArgs(tenantId);

        await CreateJob(
                source,
                distributor,
                backgroundJobManager,
                recipientBatchSize: 2,
                progressStore)
            .ExecuteAsync(args, CancellationToken.None);

        distributor.Calls.ShouldHaveSingleItem();
        backgroundJobManager.EnqueuedArgs.ShouldBeEmpty();
        var progress = await progressStore.GetAsync(args.Notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Canceled);
        progress.CompletedPageCount.ShouldBe(1);
        progress.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Empty_audience_completes_without_enqueuing_more_work()
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

        var result = await broadcaster.EnqueueAsync(
            new NotificationAudienceBroadcastRequest(
                tenantId,
                TestNotificationDefinitionProvider.Plain));
        (await broadcaster.CancelAsync(result.NotificationId, tenantId)).ShouldBeTrue();
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
        var progress = await broadcaster.GetProgressAsync(result.NotificationId, tenantId);
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

    [Fact]
    public async Task Progress_store_ignores_stale_page_and_late_failure_after_completion()
    {
        var tenantId = Guid.NewGuid();
        var args = NewJobArgs(tenantId);
        var store = new InMemoryNotificationAudienceBroadcastProgressStore();

        await store.RecordStartedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            DateTime.UtcNow);
        await store.RecordPageCompletedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            pageIndex: 0,
            candidateCount: 2,
            nextContinuationToken: "2",
            updateTime: DateTime.UtcNow);
        await store.RecordPageCompletedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            pageIndex: 1,
            candidateCount: 2,
            nextContinuationToken: null,
            updateTime: DateTime.UtcNow);
        await store.RecordCompletedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            DateTime.UtcNow);

        await store.RecordPageCompletedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            pageIndex: 0,
            candidateCount: 2,
            nextContinuationToken: "2",
            updateTime: DateTime.UtcNow);
        await store.RecordFailedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            "late-failure",
            "late failure",
            DateTime.UtcNow);

        var progress = await store.GetAsync(args.Notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Completed);
        progress.CompletedPageCount.ShouldBe(2);
        progress.CandidateCount.ShouldBe(4);
        progress.HasMore.ShouldBeFalse();
        progress.NextContinuationToken.ShouldBeNull();
        progress.FailureCode.ShouldBeNull();
        progress.FailureMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Progress_store_allows_failed_attempt_to_resume_and_complete()
    {
        var tenantId = Guid.NewGuid();
        var args = NewJobArgs(tenantId);
        var store = new InMemoryNotificationAudienceBroadcastProgressStore();

        await store.RecordStartedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            DateTime.UtcNow);
        await store.RecordFailedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            "transient-failure",
            "transient failure",
            DateTime.UtcNow);

        await store.RecordPageCompletedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            pageIndex: 0,
            candidateCount: 2,
            nextContinuationToken: null,
            updateTime: DateTime.UtcNow);
        await store.RecordCompletedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            DateTime.UtcNow);

        var progress = await store.GetAsync(args.Notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Completed);
        progress.CompletedPageCount.ShouldBe(1);
        progress.CandidateCount.ShouldBe(2);
        progress.FailureCode.ShouldBeNull();
        progress.FailureMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Progress_store_allows_cancellation_after_failed_attempt()
    {
        var tenantId = Guid.NewGuid();
        var args = NewJobArgs(tenantId);
        var store = new InMemoryNotificationAudienceBroadcastProgressStore();

        await store.RecordStartedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            DateTime.UtcNow);
        await store.RecordFailedAsync(
            args.Notification,
            args.AudienceName,
            tenantId,
            "transient-failure",
            "transient failure",
            DateTime.UtcNow);

        (await store.RequestCancellationAsync(args.Notification.Id, tenantId, DateTime.UtcNow))
            .ShouldBeTrue();

        var progress = await store.GetAsync(args.Notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.CancellationRequested);
        progress.IsCancellationRequested.ShouldBeTrue();
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
        store ??= Substitute.For<INotificationStore>();
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
            Options.Create(new NotificationAudienceBroadcastOptions()),
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
            Options.Create(new NotificationAudienceBroadcastOptions { RecipientBatchSize = recipientBatchSize }),
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
        return Substitute.For<INotificationDistributor>();
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
            continuationToken: null,
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
            var skip = string.IsNullOrWhiteSpace(request.ContinuationToken)
                ? 0
                : int.Parse(request.ContinuationToken, CultureInfo.InvariantCulture);
            var page = _userIds.Skip(skip).Take(request.MaxResultCount).ToArray();
            var nextIndex = skip + page.Length;
            return Task.FromResult(new NotificationAudienceRecipientPage(
                page,
                nextIndex < _userIds.Count ? nextIndex.ToString(CultureInfo.InvariantCulture) : null));
        }
    }

    private sealed class CancelAfterPageProgressStore : InMemoryNotificationAudienceBroadcastProgressStore
    {
        public override async Task RecordPageCompletedAsync(
            NotificationInfo notification,
            string audienceName,
            Guid? tenantId,
            long pageIndex,
            long candidateCount,
            string? nextContinuationToken,
            DateTime updateTime,
            CancellationToken cancellationToken = default)
        {
            await base.RecordPageCompletedAsync(
                notification,
                audienceName,
                tenantId,
                pageIndex,
                candidateCount,
                nextContinuationToken,
                updateTime,
                cancellationToken);
            await RequestCancellationAsync(notification.Id, tenantId, updateTime, cancellationToken);
        }
    }

    private sealed class RecordingPreparedNotificationDistributor : INotificationDistributor
    {
        public List<PreparedCall> Calls { get; } = new();

        public Task DistributeAsync(
            NotificationInfo notification,
            Guid[]? userIds = null,
            Guid[]? excludedUserIds = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
            NotificationInfo notification,
            Guid[] userIds,
            Guid[]? excludedUserIds = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DistributePreparedAsync(
            NotificationInfo notification,
            Guid[] userIds,
            NotificationRecipientEligibilityMode recipientEligibilityMode,
            CancellationToken cancellationToken = default)
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
