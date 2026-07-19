using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Provider-agnostic durable audience-broadcast state scenarios shared by EF Core and MongoDB.</summary>
public abstract class NotificationAudienceBroadcastProgressStore_Tests<TStartupModule> :
    NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task Notification_center_replaces_the_process_local_store_and_progress_survives_reload()
    {
        var tenantId = Guid.NewGuid();
        var notification = NewNotification(tenantId);
        var store = GetRequiredService<INotificationAudienceBroadcastProgressStore>();
        store.ShouldBeOfType<NotificationAudienceBroadcastProgressStore>();

        await store.RecordStartedAsync(notification, NotificationAudienceNames.AllActiveUsers, tenantId, Utc(0));
        await store.RecordPageCompletedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            tenantId,
            pageIndex: 0,
            candidateCount: 25,
            nextContinuationToken: "opaque-token",
            updateTime: Utc(1));

        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        var reloaded = await scope.ServiceProvider
            .GetRequiredService<INotificationAudienceBroadcastProgressStore>()
            .GetAsync(notification.Id, tenantId);

        reloaded.ShouldNotBeNull();
        reloaded.Status.ShouldBe(NotificationAudienceBroadcastStatus.Running);
        reloaded.CompletedPageCount.ShouldBe(1);
        reloaded.CandidateCount.ShouldBe(25);
        reloaded.NextContinuationToken.ShouldBe("opaque-token");
        reloaded.ConcurrencyStamp.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Replayed_and_concurrent_page_updates_are_counted_once()
    {
        var notification = NewNotification(tenantId: null);
        var store = GetRequiredService<INotificationAudienceBroadcastProgressStore>();
        await store.RecordStartedAsync(notification, NotificationAudienceNames.AllActiveUsers, null, Utc(0));
        var bothLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadedCount = 0;

        async Task<bool> UpdatePreloadedStateAsync()
        {
            using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();
            using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            using (dataFilter.Disable<IMultiTenant>())
            {
                var repository = scope.ServiceProvider
                    .GetRequiredService<IRepository<NotificationAudienceBroadcastState, Guid>>();
                var state = await repository.GetAsync(notification.Id);
                state.RecordPageCompleted(0, 5, "next", Utc(1)).ShouldBeTrue();
                if (Interlocked.Increment(ref loadedCount) == 2)
                {
                    bothLoaded.TrySetResult();
                }

                await bothLoaded.Task;
                try
                {
                    await repository.UpdateAsync(state, autoSave: true);
                    await unitOfWork.CompleteAsync();
                    return true;
                }
                catch (AbpDbConcurrencyException)
                {
                    return false;
                }
            }
        }

        var winners = await Task.WhenAll(UpdatePreloadedStateAsync(), UpdatePreloadedStateAsync());
        winners.Count(winner => winner).ShouldBe(1);

        await store.RecordPageCompletedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            null,
            pageIndex: 0,
            candidateCount: 5,
            nextContinuationToken: "next",
            updateTime: Utc(2));

        async Task RecordSecondPageThroughIndependentScopeAsync()
        {
            using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<INotificationAudienceBroadcastProgressStore>()
                .RecordPageCompletedAsync(
                    notification,
                    NotificationAudienceNames.AllActiveUsers,
                    null,
                    pageIndex: 1,
                    candidateCount: 7,
                    nextContinuationToken: null,
                    updateTime: Utc(3));
        }

        await Task.WhenAll(
            RecordSecondPageThroughIndependentScopeAsync(),
            RecordSecondPageThroughIndependentScopeAsync());
        var progress = await store.GetAsync(notification.Id, null);
        progress.ShouldNotBeNull();
        progress.CompletedPageCount.ShouldBe(2);
        progress.CandidateCount.ShouldBe(12);
    }

    [Fact]
    public async Task Cancellation_is_durable_and_completion_resolves_to_canceled()
    {
        var tenantId = Guid.NewGuid();
        var notification = NewNotification(tenantId);
        var store = GetRequiredService<INotificationAudienceBroadcastProgressStore>();
        await store.RecordStartedAsync(notification, NotificationAudienceNames.AllActiveUsers, tenantId, Utc(0));

        (await store.RequestCancellationAsync(notification.Id, tenantId, Utc(1))).ShouldBeTrue();
        (await store.IsCancellationRequestedAsync(notification.Id, tenantId)).ShouldBeTrue();
        await store.RecordPageCompletedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            tenantId,
            pageIndex: 0,
            candidateCount: 3,
            nextContinuationToken: "would-have-more",
            updateTime: Utc(2));
        await store.RecordCompletedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            tenantId,
            Utc(3));

        var progress = await store.GetAsync(notification.Id, tenantId);
        progress.ShouldNotBeNull();
        progress.Status.ShouldBe(NotificationAudienceBroadcastStatus.Canceled);
        progress.IsCancellationRequested.ShouldBeTrue();
        progress.CancellationRequestedTime.ShouldBe(Utc(1));
        progress.CompletedPageCount.ShouldBe(1);
        progress.NextContinuationToken.ShouldBeNull();
        progress.CompletionTime.ShouldBe(Utc(3));
    }

    [Fact]
    public async Task Completed_and_canceled_terminal_states_never_regress()
    {
        foreach (var cancel in new[] { false, true })
        {
            var notification = NewNotification(Guid.NewGuid());
            var store = GetRequiredService<INotificationAudienceBroadcastProgressStore>();
            await store.RecordStartedAsync(
                notification,
                NotificationAudienceNames.AllActiveUsers,
                notification.TenantId,
                Utc(0));
            if (cancel)
            {
                await store.RequestCancellationAsync(notification.Id, notification.TenantId, Utc(1));
                await store.RecordCanceledAsync(
                    notification,
                    NotificationAudienceNames.AllActiveUsers,
                    notification.TenantId,
                    Utc(2));
            }
            else
            {
                await store.RecordCompletedAsync(
                    notification,
                    NotificationAudienceNames.AllActiveUsers,
                    notification.TenantId,
                    Utc(2));
            }

            await store.RecordStartedAsync(
                notification,
                "changed-audience",
                notification.TenantId,
                Utc(3));
            await store.RecordPageCompletedAsync(
                notification,
                "changed-audience",
                notification.TenantId,
                0,
                100,
                "next",
                Utc(3));
            await store.RecordFailedAsync(
                notification,
                "changed-audience",
                notification.TenantId,
                "late-failure",
                "A late failure.",
                Utc(3));

            var progress = await store.GetAsync(notification.Id, notification.TenantId);
            progress.ShouldNotBeNull();
            progress.Status.ShouldBe(cancel
                ? NotificationAudienceBroadcastStatus.Canceled
                : NotificationAudienceBroadcastStatus.Completed);
            progress.CompletedPageCount.ShouldBe(0);
            progress.FailureCode.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Failed_progress_is_sanitized_and_can_resume_on_job_retry()
    {
        var notification = NewNotification(Guid.NewGuid());
        var store = GetRequiredService<INotificationAudienceBroadcastProgressStore>();
        await store.RecordStartedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            notification.TenantId,
            Utc(0));
        await store.RecordFailedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            notification.TenantId,
            "page-failed",
            "Audience broadcast page failed.",
            Utc(1));

        using (var scope = GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var reloaded = await scope.ServiceProvider
                .GetRequiredService<INotificationAudienceBroadcastProgressStore>()
                .GetAsync(notification.Id, notification.TenantId);
            reloaded.ShouldNotBeNull();
            reloaded.FailureCode.ShouldBe("page-failed");
            reloaded.FailureMessage.ShouldBe("Audience broadcast page failed.");
        }

        await store.RecordPageCompletedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            notification.TenantId,
            0,
            2,
            null,
            Utc(2));
        await store.RecordCompletedAsync(
            notification,
            NotificationAudienceNames.AllActiveUsers,
            notification.TenantId,
            Utc(3));

        var completed = await store.GetAsync(notification.Id, notification.TenantId);
        completed.ShouldNotBeNull();
        completed.Status.ShouldBe(NotificationAudienceBroadcastStatus.Completed);
        completed.FailureCode.ShouldBeNull();
        completed.FailureMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Host_and_tenant_scopes_are_explicit_and_isolated()
    {
        var tenantId = Guid.NewGuid();
        var hostNotification = NewNotification(null);
        var tenantNotification = NewNotification(tenantId);
        var store = GetRequiredService<INotificationAudienceBroadcastProgressStore>();
        await store.RecordStartedAsync(hostNotification, NotificationAudienceNames.AllActiveUsers, null, Utc(0));
        await store.RecordStartedAsync(
            tenantNotification,
            NotificationAudienceNames.AllActiveUsers,
            tenantId,
            Utc(0));

        (await store.GetAsync(hostNotification.Id, null)).ShouldNotBeNull();
        (await store.GetAsync(hostNotification.Id, tenantId)).ShouldBeNull();
        (await store.GetAsync(tenantNotification.Id, tenantId)).ShouldNotBeNull();
        (await store.GetAsync(tenantNotification.Id, null)).ShouldBeNull();
        (await store.RequestCancellationAsync(hostNotification.Id, tenantId, Utc(1))).ShouldBeFalse();
    }

    [Fact]
    public async Task Retention_cleanup_deletes_only_expired_terminal_broadcast_state()
    {
        var now = Utc(0).AddDays(120);
        var completed = NewNotification(null);
        var running = NewNotification(null);
        var recent = NewNotification(null);
        var store = GetRequiredService<INotificationAudienceBroadcastProgressStore>();
        await store.RecordStartedAsync(completed, NotificationAudienceNames.AllActiveUsers, null, Utc(0));
        await store.RecordCompletedAsync(completed, NotificationAudienceNames.AllActiveUsers, null, Utc(1));
        await store.RecordStartedAsync(running, NotificationAudienceNames.AllActiveUsers, null, Utc(0));
        await store.RecordPageCompletedAsync(
            running,
            NotificationAudienceNames.AllActiveUsers,
            null,
            0,
            1,
            "next",
            Utc(1));
        await store.RecordStartedAsync(recent, NotificationAudienceNames.AllActiveUsers, null, now.AddDays(-1));
        await store.RecordCompletedAsync(recent, NotificationAudienceNames.AllActiveUsers, null, now.AddDays(-1));

        var result = await GetRequiredService<NotificationRetentionManager>().CleanupAsync(
            new NotificationRetentionCleanupRequest { Now = now });

        result.ScannedAudienceBroadcastStates.ShouldBe(1);
        result.DeletedAudienceBroadcastStates.ShouldBe(1);
        (await store.GetAsync(completed.Id, null)).ShouldBeNull();
        (await store.GetAsync(running.Id, null)).ShouldNotBeNull();
        (await store.GetAsync(recent.Id, null)).ShouldNotBeNull();
    }

    private static NotificationInfo NewNotification(Guid? tenantId)
    {
        return new NotificationInfo
        {
            Id = Guid.NewGuid(),
            NotificationName = "Test.AudienceBroadcast",
            CreationTime = Utc(0),
            TenantId = tenantId
        };
    }

    private static DateTime Utc(int minute)
    {
        return new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc);
    }
}
