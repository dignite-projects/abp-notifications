using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryProcessorTests
{
    [Fact]
    public async Task Partial_failure_retries_only_the_failed_recipient_channel_and_duplicate_events_are_idempotent()
    {
        var now = DateTime.UtcNow;
        var calls = new ConcurrentDictionary<Guid, int>();
        var otherChannelCalls = 0;
        var failedUser = Guid.NewGuid();
        var completedUser = Guid.NewGuid();
        var notifier = new TestReliableNotifier("Email", workItem =>
        {
            var count = calls.AddOrUpdate(workItem.UserId, 1, (_, current) => current + 1);
            if (workItem.UserId == failedUser && count == 1)
            {
                throw new InvalidOperationException("sensitive-provider-secret");
            }

            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        });
        var otherChannelNotifier = new TestReliableNotifier("SignalR", _ =>
        {
            otherChannelCalls++;
            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        });
        var (processor, store) = CreateProcessor(now, new[] { notifier, otherChannelNotifier });
        var notificationId = Guid.NewGuid();
        var failed = CreateWork(notificationId, failedUser, "Email");
        var completed = CreateWork(notificationId, completedUser, "Email");
        var completedOtherChannel = CreateWork(notificationId, failedUser, "SignalR");

        await processor.ProcessAsync(failed);
        await processor.ProcessAsync(completed);
        await processor.ProcessAsync(completedOtherChannel);
        await processor.ProcessAsync(completed); // duplicate broker delivery
        await processor.ProcessAsync(completedOtherChannel); // duplicate broker delivery
        await processor.ProcessAsync(failed); // due retry

        calls[failedUser].ShouldBe(2);
        calls[completedUser].ShouldBe(1);
        otherChannelCalls.ShouldBe(1);
        (await store.GetDueWorkItemsAsync(now, 10)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_workers_execute_a_work_item_only_once_while_the_lease_is_active()
    {
        var now = DateTime.UtcNow;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var notifier = new TestReliableNotifier("Email", async _ =>
        {
            callCount++;
            started.TrySetResult();
            await release.Task;
            return NotificationDeliveryResult.Succeeded();
        });
        var (processor, _) = CreateProcessor(now, new[] { notifier });
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "Email");

        var first = processor.ProcessAsync(work);
        await started.Task;
        await processor.ProcessAsync(work);
        release.TrySetResult();
        await first;

        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Retry_exhaustion_moves_to_terminal_state_without_unbounded_notifier_calls()
    {
        var now = DateTime.UtcNow;
        var callCount = 0;
        var notifier = new TestReliableNotifier("Email", _ =>
        {
            callCount++;
            throw new InvalidOperationException("do-not-persist-this-secret");
        });
        var options = DeliveryOptions(maxAttempts: 2);
        var (processor, store) = CreateProcessor(now, new[] { notifier }, options);
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "Email");

        await processor.ProcessAsync(work);
        await processor.ProcessAsync(work);
        await processor.ProcessAsync(work);

        callCount.ShouldBe(2);
        (await store.GetDueWorkItemsAsync(now.AddDays(1), 10)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Suppression_is_terminal_but_manual_requeue_starts_a_new_attempt_cycle()
    {
        var now = DateTime.UtcNow;
        var callCount = 0;
        var notifier = new TestReliableNotifier("Email", _ =>
        {
            callCount++;
            return Task.FromResult(NotificationDeliveryResult.Suppressed("preference-disabled"));
        });
        var (processor, store) = CreateProcessor(now, new[] { notifier });
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "Email");

        await processor.ProcessAsync(work);
        await processor.ProcessAsync(work);
        callCount.ShouldBe(1);

        (await store.RequeueAsync(work.DeliveryId, work.TenantId, now)).ShouldBeTrue();
        await processor.ProcessAsync(work);
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task Legacy_notifier_receives_a_singleton_compatibility_event_with_stable_identity()
    {
        var now = DateTime.UtcNow;
        var legacy = new TestLegacyNotifier();
        var (processor, _) = CreateProcessor(
            now,
            Array.Empty<INotificationDeliveryNotifier>(),
            legacyNotifiers: new[] { legacy });
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "Legacy");

        await processor.ProcessAsync(work);

        var adapted = legacy.Received.ShouldNotBeNull();
        adapted.UserIds.ShouldBe(new[] { work.UserId });
        adapted.Channels.ShouldBe(new[] { work.Channel });
        adapted.DeliveryId.ShouldBe(work.DeliveryId);
        adapted.IdempotencyKey.ShouldBe(work.IdempotencyKey);
    }

    [Fact]
    public void Identity_is_case_insensitive_for_channels_and_tenant_bounded()
    {
        var notificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        NotificationDeliveryIdentity.CreateId(tenantId, notificationId, userId, "Email")
            .ShouldBe(NotificationDeliveryIdentity.CreateId(tenantId, notificationId, userId, " email "));
        NotificationDeliveryIdentity.CreateIdempotencyKey(tenantId, notificationId, userId, "Email")
            .ShouldBe(NotificationDeliveryIdentity.CreateIdempotencyKey(tenantId, notificationId, userId, "EMAIL"));
        NotificationDeliveryIdentity.CreateId(null, notificationId, userId, "Email")
            .ShouldNotBe(NotificationDeliveryIdentity.CreateId(tenantId, notificationId, userId, "Email"));
    }

    [Fact]
    public async Task A_process_that_does_not_host_the_work_channel_does_not_create_or_fail_delivery_state()
    {
        var now = DateTime.UtcNow;
        var (processor, store) = CreateProcessor(now, Array.Empty<INotificationDeliveryNotifier>());
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "RemoteEmail");

        await processor.ProcessAsync(work);

        (await store.GetDueWorkItemsAsync(now.AddDays(1), 10)).ShouldBeEmpty();
    }

    private static (NotificationDeliveryProcessor Processor, NullNotificationDeliveryStore Store) CreateProcessor(
        DateTime now,
        INotificationDeliveryNotifier[] reliableNotifiers,
        NotificationOptions? options = null,
        INotificationNotifier<NotificationDeliveryEto>[]? legacyNotifiers = null)
    {
        options ??= DeliveryOptions();
        var optionWrapper = Options.Create(options);
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(_ => now);
        var store = new NullNotificationDeliveryStore();
        return (new NotificationDeliveryProcessor(
            store,
            reliableNotifiers,
            legacyNotifiers ?? Array.Empty<INotificationNotifier<NotificationDeliveryEto>>(),
            new NotificationDeliveryRetryPolicy(optionWrapper),
            clock,
            new TestCurrentTenant(),
            optionWrapper,
            NullLogger<NotificationDeliveryProcessor>.Instance), store);
    }

    private static NotificationOptions DeliveryOptions(int maxAttempts = 3)
    {
        return new NotificationOptions
        {
            MaxDeliveryAttempts = maxAttempts,
            DeliveryLeaseDuration = TimeSpan.FromMinutes(2),
            InitialDeliveryRetryDelay = TimeSpan.Zero,
            MaxDeliveryRetryDelay = TimeSpan.Zero,
            DeliveryRetryJitterFactor = 0
        };
    }

    private static NotificationDeliveryWorkEto CreateWork(
        Guid notificationId,
        Guid userId,
        string channel)
    {
        return new NotificationDeliveryWorkEto
        {
            DeliveryId = NotificationDeliveryIdentity.CreateId(null, notificationId, userId, channel),
            IdempotencyKey = NotificationDeliveryIdentity.CreateIdempotencyKey(
                null,
                notificationId,
                userId,
                channel),
            NotificationId = notificationId,
            NotificationName = "test",
            CreationTime = DateTime.UtcNow,
            UserId = userId,
            Channel = channel
        };
    }

    private sealed class TestReliableNotifier : INotificationDeliveryNotifier
    {
        private readonly Func<NotificationDeliveryWorkEto, Task<NotificationDeliveryResult>> _deliver;

        public string Name { get; }

        public TestReliableNotifier(
            string name,
            Func<NotificationDeliveryWorkEto, Task<NotificationDeliveryResult>> deliver)
        {
            Name = name;
            _deliver = deliver;
        }

        public Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryWorkEto workItem)
        {
            return _deliver(workItem);
        }
    }

    private sealed class TestLegacyNotifier : INotificationNotifier<NotificationDeliveryEto>
    {
        public string Name => "Legacy";

        public NotificationDeliveryEto? Received { get; private set; }

        public Task HandleEventAsync(NotificationDeliveryEto eventData)
        {
            Received = eventData;
            return Task.CompletedTask;
        }
    }
}
