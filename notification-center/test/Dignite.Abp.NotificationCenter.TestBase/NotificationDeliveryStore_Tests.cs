using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Identical delivery-state, lease and operator scenarios run against EF Core and MongoDB.</summary>
public abstract class NotificationDeliveryStore_Tests<TStartupModule> : NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public void Operator_query_retry_and_force_delivery_have_separate_permission_boundaries()
    {
        typeof(NotificationDeliveryAppService)
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy.ShouldBe(NotificationCenterPermissions.Deliveries.Default);
        typeof(NotificationDeliveryAppService)
            .GetMethod(nameof(NotificationDeliveryAppService.RetryAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy.ShouldBe(NotificationCenterPermissions.Deliveries.Retry);
        typeof(NotificationDeliveryAppService)
            .GetMethod(nameof(NotificationDeliveryAppService.ForceDeliverAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy.ShouldBe(NotificationCenterPermissions.Deliveries.ForceDeliver);
        NotificationCenterPermissions.Deliveries.ForceDeliver
            .ShouldNotBe(NotificationCenterPermissions.Deliveries.Retry);
    }

    [Fact]
    public async Task Stable_identity_and_unique_index_prevent_duplicate_recipient_channel_rows()
    {
        var work = await CreateWorkAsync(channel: "Email");
        var duplicate = CreateWork(
            work.NotificationId,
            work.UserId,
            "email",
            work.TenantId,
            work.CreationTime);

        await WithTenantUnitOfWorkAsync(work.TenantId, async () =>
        {
            var store = GetRequiredService<INotificationDeliveryStore>();
            await store.EnsureCreatedAsync(work);
            await store.EnsureCreatedAsync(duplicate);
        });

        (await GetRecordsAsync(work.TenantId, work.NotificationId)).Count.ShouldBe(1);
        duplicate.DeliveryId.ShouldBe(work.DeliveryId);
        duplicate.IdempotencyKey.ShouldBe(work.IdempotencyKey);

        await Should.ThrowAsync<Exception>(() => WithTenantUnitOfWorkAsync(work.TenantId, async () =>
        {
            await GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>().InsertAsync(
                new NotificationDeliveryRecord(
                    Guid.NewGuid(),
                    work.NotificationId,
                    work.UserId,
                    "EMAIL",
                    work.IdempotencyKey,
                    work.NotificationName,
                    data: null,
                    work.EntityTypeName,
                    work.EntityId,
                    work.Severity,
                    work.CreationTime,
                    work.TenantId),
                autoSave: true);
        }));
    }

    [Fact]
    public async Task Concurrent_workers_atomically_claim_once_and_an_expired_lease_is_recoverable()
    {
        var now = DateTime.UtcNow;
        var work = await CreateAndPersistWorkAsync(now: now);
        var claims = await Task.WhenAll(
            ClaimInScopeAsync(work, now),
            ClaimInScopeAsync(work, now));

        claims.Count(claim => claim != null).ShouldBe(1);
        claims.Single(claim => claim != null)!.AttemptCount.ShouldBe(1);
        (await ClaimInScopeAsync(work, now.AddSeconds(30))).ShouldBeNull();

        var recovered = await ClaimInScopeAsync(work, now.AddMinutes(3));
        recovered.ShouldNotBeNull();
        recovered!.AttemptCount.ShouldBe(2);
        recovered.LeaseId.ShouldNotBe(claims.Single(claim => claim != null)!.LeaseId);
        (await GetRequiredService<INotificationDeliveryStore>().MarkSucceededAsync(
            work.DeliveryId,
            work.TenantId,
            recovered.LeaseId,
            now.AddMinutes(3))).ShouldBeTrue();
        (await ClaimInScopeAsync(work, now.AddMinutes(4))).ShouldBeNull();
        (await GetRecordsAsync(work.TenantId, work.NotificationId)).Single().State
            .ShouldBe(NotificationDeliveryState.Succeeded);
    }

    [Fact]
    public async Task Concurrent_first_events_materialize_one_row_and_only_one_claim_wins()
    {
        var now = DateTime.UtcNow;
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "FirstEmail", tenantId: null, now);
        using var beforeInsert = new Barrier(participantCount: 2);

        var claims = await Task.WhenAll(
            Task.Run(() => MaterializeAndClaimInScopeAsync(work, now, beforeInsert)),
            Task.Run(() => MaterializeAndClaimInScopeAsync(work, now, beforeInsert)));

        claims.Count(claim => claim != null).ShouldBe(1);
        (await GetRecordsAsync(work.TenantId, work.NotificationId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Existing_claim_provider_failure_is_not_misclassified_as_a_lost_initial_insert()
    {
        var now = DateTime.UtcNow;
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "FailingEmail", tenantId: null, now);
        await WithTenantUnitOfWorkAsync(work.TenantId, () =>
            GetRequiredService<INotificationDeliveryStore>().EnsureCreatedAsync(work));

        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var store = new FailingClaimUpdateNotificationDeliveryStore(
            serviceProvider.GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>(),
            serviceProvider.GetRequiredService<INotificationDataSerializer>(),
            serviceProvider.GetRequiredService<IGuidGenerator>(),
            serviceProvider.GetRequiredService<IAsyncQueryableExecuter>(),
            serviceProvider.GetRequiredService<IDataFilter>(),
            serviceProvider.GetRequiredService<IUnitOfWorkManager>());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnsureCreatedAndTryClaimAsync(
                work,
                now,
                TimeSpan.FromMinutes(2),
                3));

        exception.Message.ShouldBe("simulated-claim-provider-failure");
        (await GetRecordsAsync(work.TenantId, work.NotificationId)).Single().State
            .ShouldBe(NotificationDeliveryState.Pending);
    }

    [Fact]
    public async Task First_consumer_event_materializes_claims_and_delivers_inside_an_ambient_transaction()
    {
        var work = CreateWork(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ImmediateEmail",
            tenantId: null,
            DateTime.UtcNow);
        var notifier = new RecordingDeliveryNotifier(work.Channel);
        var deliveryOptions = Options.Create(new NotificationOptions
        {
            IsDeliveryRetryWorkerEnabled = false,
            DeliveryRetryJitterFactor = 0
        });
        var processor = new NotificationDeliveryProcessor(
            GetRequiredService<INotificationDeliveryStore>(),
            new[] { notifier },
            Array.Empty<INotificationNotifier<NotificationDeliveryEto>>(),
            new NotificationDeliveryRetryPolicy(deliveryOptions),
            GetRequiredService<IClock>(),
            GetRequiredService<ICurrentTenant>(),
            deliveryOptions,
            NullLogger<NotificationDeliveryProcessor>.Instance);

        using (var outerUnitOfWork = GetRequiredService<IUnitOfWorkManager>().Begin(
                   requiresNew: true,
                   isTransactional: true))
        {
            await processor.ProcessAsync(work);

            notifier.InvocationCount.ShouldBe(1);
            await outerUnitOfWork.CompleteAsync();
        }

        var record = (await GetRecordsAsync(work.TenantId, work.NotificationId)).Single();
        record.State.ShouldBe(NotificationDeliveryState.Succeeded);
        record.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrency_stamp_forces_two_preloaded_claim_updates_to_have_exactly_one_winner()
    {
        var now = DateTime.UtcNow;
        var work = await CreateAndPersistWorkAsync(now: now);
        var bothLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadedCount = 0;

        async Task<bool> UpdatePreloadedRecordAsync()
        {
            using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            var repository = scope.ServiceProvider
                .GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>();
            var record = await repository.GetAsync(work.DeliveryId);
            record.Claim(Guid.NewGuid(), now, TimeSpan.FromMinutes(2));
            if (Interlocked.Increment(ref loadedCount) == 2)
            {
                bothLoaded.TrySetResult();
            }

            await bothLoaded.Task;
            try
            {
                await repository.UpdateAsync(record, autoSave: true);
                await unitOfWork.CompleteAsync();
                return true;
            }
            catch (AbpDbConcurrencyException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(UpdatePreloadedRecordAsync(), UpdatePreloadedRecordAsync());

        results.Count(result => result).ShouldBe(1);
    }

    [Fact]
    public async Task Durable_retry_reconstructs_a_self_contained_work_item_without_a_local_notification_row()
    {
        var now = DateTime.UtcNow;
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "RemoteEmail", tenantId: null, now);
        var store = GetRequiredService<INotificationDeliveryStore>();
        await WithTenantUnitOfWorkAsync(work.TenantId, () => store.EnsureCreatedAsync(work));
        var claim = (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now,
            TimeSpan.FromMinutes(2),
            3))!;
        await store.MarkFailedAsync(
            work.DeliveryId,
            work.TenantId,
            claim.LeaseId,
            now,
            "channel-execution-failed",
            now.AddMinutes(1));

        var reconstructed = (await store.GetDueWorkItemsAsync(now.AddMinutes(1), 10))
            .Single(item => item.DeliveryId == work.DeliveryId);

        reconstructed.NotificationName.ShouldBe(work.NotificationName);
        reconstructed.Data.ShouldBeOfType<OrderShippedNotificationData>().OrderNumber.ShouldBe("SO-DELIVERY");
        reconstructed.UserId.ShouldBe(work.UserId);
        reconstructed.Channel.ShouldBe(work.Channel);
    }

    [Fact]
    public async Task Producer_delayed_intent_is_durable_and_not_claimable_before_not_before()
    {
        var now = DateTime.UtcNow;
        var work = await CreateWorkAsync(now: now);
        work.Intent = NotificationDeliveryIntent.Delay;
        work.DeliveryNotBefore = now.AddHours(2);
        work.PreferenceReasonCode = NotificationDeliveryPreferenceReasonCodes.QuietHours;
        var store = GetRequiredService<INotificationDeliveryStore>();

        (await store.EnsureCreatedAndTryClaimAsync(
            work,
            now,
            TimeSpan.FromMinutes(2),
            3)).ShouldBeNull();
        (await store.GetDueWorkItemsAsync(now.AddHours(1), 10))
            .ShouldNotContain(item => item.DeliveryId == work.DeliveryId);

        var due = (await store.GetDueWorkItemsAsync(now.AddHours(2), 10))
            .Single(item => item.DeliveryId == work.DeliveryId);
        due.Intent.ShouldBe(NotificationDeliveryIntent.Delay);
        due.DeliveryNotBefore.ShouldNotBeNull().ShouldBe(
            work.DeliveryNotBefore.ShouldNotBeNull(),
            TimeSpan.FromMilliseconds(1));
        due.PreferenceReasonCode.ShouldBe(NotificationDeliveryPreferenceReasonCodes.QuietHours);
        (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now.AddHours(2),
            TimeSpan.FromMinutes(2),
            3)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Retry_timing_exhaustion_and_sanitized_failure_are_observable()
    {
        var now = DateTime.UtcNow;
        var work = await CreateAndPersistWorkAsync(now: now);
        var store = GetRequiredService<INotificationDeliveryStore>();
        var first = (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now,
            TimeSpan.FromMinutes(2),
            2))!;
        var retryAt = now.AddMinutes(1);
        (await store.MarkFailedAsync(
            work.DeliveryId,
            work.TenantId,
            first.LeaseId,
            now,
            "channel-execution-failed",
            retryAt)).ShouldBeTrue();

        (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now.AddSeconds(59),
            TimeSpan.FromMinutes(2),
            2)).ShouldBeNull();
        var second = (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            retryAt,
            TimeSpan.FromMinutes(2),
            2))!;
        second.AttemptCount.ShouldBe(2);
        (await store.MarkFailedAsync(
            work.DeliveryId,
            work.TenantId,
            second.LeaseId,
            retryAt,
            "channel-execution-failed",
            nextAttemptTime: null)).ShouldBeTrue();

        var record = (await GetRecordsAsync(work.TenantId, work.NotificationId)).Single();
        record.State.ShouldBe(NotificationDeliveryState.DeadLettered);
        record.AttemptCount.ShouldBe(2);
        record.NextAttemptTime.ShouldBeNull();
        record.LastFailureCode.ShouldBe("channel-execution-failed");
        record.LastFailureMessage.ShouldBe("The channel failed to deliver this notification.");
        record.LastFailureMessage!.ShouldNotContain("secret");
    }

    [Fact]
    public async Task Suppressed_delivery_is_terminal_and_ordinary_retry_is_rejected()
    {
        var now = DateTime.UtcNow;
        var work = await CreateAndPersistWorkAsync(now: now);
        var store = GetRequiredService<INotificationDeliveryStore>();
        var claim = (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now,
            TimeSpan.FromMinutes(2),
            3))!;
        (await store.MarkSuppressedAsync(
            work.DeliveryId,
            work.TenantId,
            claim.LeaseId,
            now,
            "preference-disabled")).ShouldBeTrue();
        (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now.AddHours(1),
            TimeSpan.FromMinutes(2),
            3)).ShouldBeNull();

        (await store.RetryAsync(work.DeliveryId, work.TenantId, now.AddHours(1))).ShouldBeFalse();
        var record = (await GetRecordsAsync(work.TenantId, work.NotificationId)).Single();
        record.State.ShouldBe(NotificationDeliveryState.Suppressed);
        record.LastFailureCode.ShouldBe("preference-disabled");
    }

    [Fact]
    public async Task Force_delivery_overrides_suppression_and_records_non_sensitive_audit_data()
    {
        var now = DateTime.UtcNow;
        var work = await CreateWorkAsync(now: now);
        work.Intent = NotificationDeliveryIntent.Suppress;
        work.PreferenceReasonCode = "preference-disabled";
        await WithTenantUnitOfWorkAsync(work.TenantId, () =>
            GetRequiredService<INotificationDeliveryStore>().EnsureCreatedAsync(work));

        var store = GetRequiredService<INotificationDeliveryStore>();
        var claim = (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now,
            TimeSpan.FromMinutes(2),
            3))!;
        (await store.MarkSuppressedAsync(
            work.DeliveryId,
            work.TenantId,
            claim.LeaseId,
            now,
            "preference-disabled")).ShouldBeTrue();

        var actorId = Guid.NewGuid();
        var forcedAt = now.AddHours(1);
        (await store.ForceDeliverAsync(
            work.DeliveryId,
            work.TenantId,
            actorId,
            forcedAt,
            NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery)).ShouldBeTrue();

        var due = await store.GetDueWorkItemsAsync(forcedAt, 10);
        var requeued = due.Single(item => item.DeliveryId == work.DeliveryId);
        requeued.Intent.ShouldBe(NotificationDeliveryIntent.Deliver);
        requeued.DeliveryNotBefore.ShouldBeNull();
        requeued.PreferenceReasonCode.ShouldBeNull();

        var record = (await GetRecordsAsync(work.TenantId, work.NotificationId)).Single();
        record.Intent.ShouldBe(NotificationDeliveryIntent.Deliver);
        record.PreferenceReasonCode.ShouldBeNull();
        record.LastForceDeliveryActorId.ShouldBe(actorId);
        record.LastForceDeliveryTime.ShouldNotBeNull();
        record.LastForceDeliveryTime.Value.ShouldBe(forcedAt, TimeSpan.FromMilliseconds(1));
        record.LastForceDeliveryPreviousState.ShouldBe(NotificationDeliveryState.Suppressed);
        record.LastForceDeliveryReasonCode.ShouldBe(
            NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery);
        record.Data!.ShouldNotContain(NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery);
    }

    [Fact]
    public async Task Retry_preserves_the_producer_intent_and_preference_diagnostics()
    {
        var now = DateTime.UtcNow;
        var work = await CreateWorkAsync(now: now);
        work.Intent = NotificationDeliveryIntent.Delay;
        work.DeliveryNotBefore = now.AddHours(1);
        work.PreferenceReasonCode = NotificationDeliveryPreferenceReasonCodes.QuietHours;
        await WithTenantUnitOfWorkAsync(work.TenantId, () =>
            GetRequiredService<INotificationDeliveryStore>().EnsureCreatedAsync(work));

        var store = GetRequiredService<INotificationDeliveryStore>();
        var claim = (await store.TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            work.DeliveryNotBefore.Value,
            TimeSpan.FromMinutes(2),
            3))!;
        await store.MarkFailedAsync(
            work.DeliveryId,
            work.TenantId,
            claim.LeaseId,
            work.DeliveryNotBefore.Value,
            "channel-execution-failed",
            nextAttemptTime: now.AddHours(3));

        (await store.RetryAsync(work.DeliveryId, work.TenantId, now.AddHours(2))).ShouldBeTrue();

        var record = (await GetRecordsAsync(work.TenantId, work.NotificationId)).Single();
        record.Intent.ShouldBe(NotificationDeliveryIntent.Delay);
        record.DeliveryNotBefore.ShouldNotBeNull();
        record.DeliveryNotBefore.Value.ShouldBe(
            work.DeliveryNotBefore.ShouldNotBeNull(),
            TimeSpan.FromMilliseconds(1));
        record.PreferenceReasonCode.ShouldBe(NotificationDeliveryPreferenceReasonCodes.QuietHours);
    }

    [Fact]
    public async Task Concurrent_force_delivery_has_one_winner_and_tenant_scope_is_authoritative()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var work = await CreateAndPersistWorkAsync(tenantId, now: now);
        var store = GetRequiredService<INotificationDeliveryStore>();
        var claim = (await store.TryClaimAsync(
            work.DeliveryId,
            tenantId,
            now,
            TimeSpan.FromMinutes(2),
            3))!;
        await store.MarkSuppressedAsync(
            work.DeliveryId,
            tenantId,
            claim.LeaseId,
            now,
            "preference-disabled");

        (await store.ForceDeliverAsync(
            work.DeliveryId,
            otherTenantId,
            Guid.NewGuid(),
            now,
            NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery)).ShouldBeFalse();

        var actorIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var results = await Task.WhenAll(actorIds.Select(actorId =>
            ForceDeliverInScopeAsync(work, actorId, now.AddMinutes(1))));
        results.Count(result => result).ShouldBe(1);

        var record = (await GetRecordsAsync(tenantId, work.NotificationId)).Single();
        actorIds.ShouldContain(record.LastForceDeliveryActorId.ShouldNotBeNull());
    }

    [Fact]
    public async Task Operator_api_rejects_suppressed_retry_and_force_delivery_records_the_current_actor()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var work = await CreateAndPersistWorkAsync(tenantId, now: now);
        var store = GetRequiredService<INotificationDeliveryStore>();
        var claim = (await store.TryClaimAsync(
            work.DeliveryId,
            tenantId,
            now,
            TimeSpan.FromMinutes(2),
            3))!;
        await store.MarkSuppressedAsync(
            work.DeliveryId,
            tenantId,
            claim.LeaseId,
            now,
            "preference-disabled");

        using (GetRequiredService<ICurrentTenant>().Change(tenantId, "tenant"))
        using (ChangeCurrentUser(actorId))
        {
            var appService = GetRequiredService<INotificationDeliveryAppService>();
            var exception = await Should.ThrowAsync<Volo.Abp.BusinessException>(
                () => appService.RetryAsync(work.DeliveryId));
            exception.Code.ShouldBe(NotificationCenterErrorCodes.SuppressedDeliveryCannotBeRetried);

            await appService.ForceDeliverAsync(work.DeliveryId);
            var dto = (await appService.GetListAsync(new GetNotificationDeliveryListInput
            {
                NotificationId = work.NotificationId,
                MaxResultCount = 10
            })).Items.Single();
            dto.LastForceDeliveryActorId.ShouldBe(actorId);
            dto.LastForceDeliveryPreviousState.ShouldBe(NotificationDeliveryState.Suppressed);
            dto.LastForceDeliveryReasonCode.ShouldBe(
                NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery);
        }

        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid(), "other-tenant"))
        using (ChangeCurrentUser(actorId))
        {
            var appService = GetRequiredService<INotificationDeliveryAppService>();
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(
                () => appService.ForceDeliverAsync(work.DeliveryId));
        }
    }

    [Fact]
    public async Task Due_scan_reconstructs_payload_and_operator_api_queries_and_retries_within_tenant()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var work = await CreateAndPersistWorkAsync(tenantId, now: now);
        var due = await GetRequiredService<INotificationDeliveryStore>()
            .GetDueWorkItemsAsync(now, 10);
        var reconstructed = due.Single(item => item.DeliveryId == work.DeliveryId);
        reconstructed.TenantId.ShouldBe(tenantId);
        reconstructed.UserId.ShouldBe(work.UserId);
        reconstructed.Channel.ShouldBe(work.Channel);
        reconstructed.Data.ShouldBeOfType<OrderShippedNotificationData>().OrderNumber.ShouldBe("SO-DELIVERY");

        var store = GetRequiredService<INotificationDeliveryStore>();
        var claim = (await store.TryClaimAsync(
            work.DeliveryId,
            tenantId,
            now,
            TimeSpan.FromMinutes(2),
            1))!;
        await store.MarkFailedAsync(
            work.DeliveryId,
            tenantId,
            claim.LeaseId,
            now,
            "channel-execution-failed",
            nextAttemptTime: null);

        using (GetRequiredService<ICurrentTenant>().Change(tenantId, "tenant"))
        {
            var appService = GetRequiredService<INotificationDeliveryAppService>();
            var page = await appService.GetListAsync(new GetNotificationDeliveryListInput
            {
                NotificationId = work.NotificationId,
                State = NotificationDeliveryState.DeadLettered,
                MaxResultCount = 10
            });
            page.TotalCount.ShouldBe(1);
            var dto = page.Items.Single();
            dto.IdempotencyKey.ShouldBe(work.IdempotencyKey);
            dto.LastFailureMessage.ShouldBe("The channel failed to deliver this notification.");

            await appService.RetryAsync(work.DeliveryId);
        }

        (await GetRecordsAsync(tenantId, work.NotificationId)).Single().State
            .ShouldBe(NotificationDeliveryState.Pending);
        (await store.RetryAsync(work.DeliveryId, tenantId: null, now.AddHours(1))).ShouldBeFalse();
    }

    private async Task<NotificationDeliveryRequestedEto> CreateAndPersistWorkAsync(
        Guid? tenantId = null,
        string channel = "Email",
        DateTime? now = null)
    {
        var work = await CreateWorkAsync(tenantId, channel, now);
        await WithTenantUnitOfWorkAsync(tenantId, () =>
            GetRequiredService<INotificationDeliveryStore>().EnsureCreatedAsync(work));
        return work;
    }

    private async Task<NotificationDeliveryRequestedEto> CreateWorkAsync(
        Guid? tenantId = null,
        string channel = "Email",
        DateTime? now = null)
    {
        var creationTime = now ?? DateTime.UtcNow;
        var notificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await WithTenantUnitOfWorkAsync(tenantId, () =>
            GetRequiredService<INotificationStore>().InsertNotificationAsync(new NotificationInfo
            {
                Id = notificationId,
                NotificationName = "order.shipped",
                Data = new OrderShippedNotificationData
                {
                    OrderNumber = "SO-DELIVERY",
                    ItemCount = 1
                },
                Severity = NotificationSeverity.Success,
                CreationTime = creationTime,
                TenantId = tenantId
            }));
        return CreateWork(notificationId, userId, channel, tenantId, creationTime);
    }

    private static NotificationDeliveryRequestedEto CreateWork(
        Guid notificationId,
        Guid userId,
        string channel,
        Guid? tenantId,
        DateTime creationTime)
    {
        return new NotificationDeliveryRequestedEto
        {
            DeliveryId = NotificationDeliveryIdentity.CreateId(tenantId, notificationId, userId, channel),
            IdempotencyKey = NotificationDeliveryIdentity.CreateIdempotencyKey(
                tenantId,
                notificationId,
                userId,
                channel),
            NotificationId = notificationId,
            NotificationName = "order.shipped",
            Data = new OrderShippedNotificationData { OrderNumber = "SO-DELIVERY", ItemCount = 1 },
            Severity = NotificationSeverity.Success,
            CreationTime = creationTime,
            UserId = userId,
            Channel = channel,
            TenantId = tenantId
        };
    }

    private async Task<NotificationDeliveryClaim?> ClaimInScopeAsync(
        NotificationDeliveryRequestedEto work,
        DateTime now)
    {
        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationDeliveryStore>().TryClaimAsync(
            work.DeliveryId,
            work.TenantId,
            now,
            TimeSpan.FromMinutes(2),
            3);
    }

    private async Task<NotificationDeliveryClaim?> MaterializeAndClaimInScopeAsync(
        NotificationDeliveryRequestedEto work,
        DateTime now,
        Barrier? beforeInsert = null)
    {
        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var store = beforeInsert == null
            ? serviceProvider.GetRequiredService<INotificationDeliveryStore>()
            : new BarrierNotificationDeliveryStore(
                serviceProvider.GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>(),
                serviceProvider.GetRequiredService<INotificationDataSerializer>(),
                serviceProvider.GetRequiredService<IGuidGenerator>(),
                serviceProvider.GetRequiredService<IAsyncQueryableExecuter>(),
                serviceProvider.GetRequiredService<IDataFilter>(),
                serviceProvider.GetRequiredService<IUnitOfWorkManager>(),
                beforeInsert);
        return await store.EnsureCreatedAndTryClaimAsync(
            work,
            now,
            TimeSpan.FromMinutes(2),
            3);
    }

    private async Task<bool> ForceDeliverInScopeAsync(
        NotificationDeliveryRequestedEto work,
        Guid actorId,
        DateTime now)
    {
        using var scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationDeliveryStore>().ForceDeliverAsync(
            work.DeliveryId,
            work.TenantId,
            actorId,
            now,
            NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery);
    }

    private async Task<System.Collections.Generic.List<NotificationDeliveryRecord>> GetRecordsAsync(
        Guid? tenantId,
        Guid notificationId)
    {
        System.Collections.Generic.List<NotificationDeliveryRecord>? records = null;
        await WithTenantUnitOfWorkAsync(tenantId, async () =>
        {
            records = await GetRequiredService<IRepository<NotificationDeliveryRecord, Guid>>()
                .GetListAsync(record => record.NotificationId == notificationId);
        });
        return records!;
    }

    private sealed class RecordingDeliveryNotifier : INotificationDeliveryNotifier
    {
        private int _invocationCount;

        public string Name { get; }

        public int InvocationCount => _invocationCount;

        public RecordingDeliveryNotifier(string name)
        {
            Name = name;
        }

        public Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequestedEto workItem)
        {
            Interlocked.Increment(ref _invocationCount);
            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        }
    }

    private sealed class BarrierNotificationDeliveryStore : NotificationDeliveryStore
    {
        private readonly Barrier _beforeInsert;

        public BarrierNotificationDeliveryStore(
            IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
            INotificationDataSerializer dataSerializer,
            IGuidGenerator guidGenerator,
            IAsyncQueryableExecuter asyncExecuter,
            IDataFilter dataFilter,
            IUnitOfWorkManager unitOfWorkManager,
            Barrier beforeInsert)
            : base(
                deliveryRepository,
                dataSerializer,
                guidGenerator,
                asyncExecuter,
                dataFilter,
                unitOfWorkManager)
        {
            _beforeInsert = beforeInsert;
        }

        protected override NotificationDeliveryRecord CreateRecord(NotificationDeliveryRequestedEto workItem)
        {
            if (!_beforeInsert.SignalAndWait(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("Both first-delivery workers did not reach the insert barrier.");
            }

            return base.CreateRecord(workItem);
        }
    }

    private sealed class FailingClaimUpdateNotificationDeliveryStore : NotificationDeliveryStore
    {
        public FailingClaimUpdateNotificationDeliveryStore(
            IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
            INotificationDataSerializer dataSerializer,
            IGuidGenerator guidGenerator,
            IAsyncQueryableExecuter asyncExecuter,
            IDataFilter dataFilter,
            IUnitOfWorkManager unitOfWorkManager)
            : base(
                deliveryRepository,
                dataSerializer,
                guidGenerator,
                asyncExecuter,
                dataFilter,
                unitOfWorkManager)
        {
        }

        protected override Task<NotificationDeliveryRecord> UpdateProcessingDeliveryAsync(
            NotificationDeliveryRecord delivery,
            System.Threading.CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("simulated-claim-provider-failure");
        }
    }

    private async Task WithTenantUnitOfWorkAsync(Guid? tenantId, Func<Task> action)
    {
        using (GetRequiredService<ICurrentTenant>().Change(tenantId, tenantId.HasValue ? "tenant" : null))
        {
            await WithUnitOfWorkAsync(action);
        }
    }
}
