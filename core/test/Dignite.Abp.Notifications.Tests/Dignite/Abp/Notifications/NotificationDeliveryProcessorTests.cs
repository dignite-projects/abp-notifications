using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Timing;
using Volo.Abp.Threading;
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
    public async Task Suppression_is_terminal_but_explicit_force_delivery_starts_a_new_attempt_cycle()
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

        (await store.RetryAsync(work.DeliveryId, work.TenantId, now)).ShouldBeFalse();
        (await store.ForceDeliverAsync(
            work.DeliveryId,
            work.TenantId,
            Guid.NewGuid(),
            now,
            NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery)).ShouldBeTrue();
        await processor.ProcessAsync(work);
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task Cancellation_propagates_to_the_notifier_and_is_not_recorded_as_a_channel_failure()
    {
        var now = DateTime.UtcNow;
        using var cancellation = new CancellationTokenSource();
        CancellationToken receivedToken = default;
        var notifier = new TestReliableNotifier("Email", (_, cancellationToken) =>
        {
            receivedToken = cancellationToken;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        });
        var (processor, store) = CreateProcessor(now, new INotificationNotifier[] { notifier });
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "Email");

        await Should.ThrowAsync<OperationCanceledException>(
            () => processor.ProcessAsync(work, cancellation.Token));

        receivedToken.ShouldBe(cancellation.Token);
        (await store.GetDueWorkItemsAsync(now, 10)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Distributed_event_adapter_passes_the_ambient_cancellation_token_to_the_processor()
    {
        var now = DateTime.UtcNow;
        using var cancellation = new CancellationTokenSource();
        CancellationToken receivedToken = default;
        var notifier = new TestReliableNotifier("Email", (request, cancellationToken) =>
        {
            receivedToken = cancellationToken;
            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        });
        var (processor, _) = CreateProcessor(now, new INotificationNotifier[] { notifier });
        var tokenProvider = Substitute.For<ICancellationTokenProvider>();
        tokenProvider.Token.Returns(cancellation.Token);
        var handler = new NotificationDeliveryRequestedHandler(processor, tokenProvider);

        await handler.HandleEventAsync(CreateWork(Guid.NewGuid(), Guid.NewGuid(), "Email"));

        receivedToken.ShouldBe(cancellation.Token);
    }

    [Fact]
    public async Task Duplicate_exposure_of_the_same_implementation_and_channel_executes_once()
    {
        var callCount = 0;
        var notifier = new TestReliableNotifier("Email", _ =>
        {
            callCount++;
            return Task.FromResult(NotificationDeliveryResult.Succeeded());
        });
        var (processor, _) = CreateProcessor(
            DateTime.UtcNow,
            new INotificationNotifier[] { notifier, notifier });

        await processor.ProcessAsync(CreateWork(Guid.NewGuid(), Guid.NewGuid(), "email"));

        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task Distinct_implementations_with_the_same_case_insensitive_channel_are_rejected()
    {
        var first = new TestReliableNotifier(
            "Email",
            _ => Task.FromResult(NotificationDeliveryResult.Succeeded()));
        var second = new OtherTestNotifier("eMaIl");
        var (processor, _) = CreateProcessor(
            DateTime.UtcNow,
            new INotificationNotifier[] { first, second });

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => processor.ProcessAsync(CreateWork(Guid.NewGuid(), Guid.NewGuid(), "EMAIL")));

        exception.Message.ShouldContain("Multiple notification notifiers");
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
        var (processor, store) = CreateProcessor(now, Array.Empty<INotificationNotifier>());
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "RemoteEmail");

        await processor.ProcessAsync(work);

        (await store.GetDueWorkItemsAsync(now.AddDays(1), 10)).ShouldBeEmpty();
    }

    [Fact]
    public async Task First_delivery_uses_the_atomic_materialize_and_claim_contract()
    {
        var now = DateTime.UtcNow;
        var work = CreateWork(Guid.NewGuid(), Guid.NewGuid(), "Email");
        var store = Substitute.For<INotificationDeliveryStore>();
        var leaseId = Guid.NewGuid();
        store.EnsureCreatedAndTryClaimAsync(
                work,
                now,
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(new NotificationDeliveryClaim(leaseId, 1, now.AddMinutes(2)));
        store.MarkSucceededAsync(
                work.DeliveryId,
                work.TenantId,
                leaseId,
                now,
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(true);
        var notifier = new TestReliableNotifier(
            "Email",
            _ => Task.FromResult(NotificationDeliveryResult.Succeeded()));
        var options = Options.Create(DeliveryOptions());
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(now);
        var processor = new NotificationDeliveryProcessor(
            store,
            new[] { notifier },
            new NotificationDeliveryRetryPolicy(options),
            clock,
            new TestCurrentTenant(),
            options,
            NullLogger<NotificationDeliveryProcessor>.Instance);

        await processor.ProcessAsync(work);

        await store.Received(1).EnsureCreatedAndTryClaimAsync(
            work,
            now,
            Arg.Any<TimeSpan>(),
            Arg.Any<int>(),
            Arg.Any<System.Threading.CancellationToken>());
        await store.DidNotReceive().EnsureCreatedAsync(
            Arg.Any<NotificationDeliveryRequestedEto>(),
            Arg.Any<System.Threading.CancellationToken>());
        await store.DidNotReceive().TryClaimAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<DateTime>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<int>(),
            Arg.Any<System.Threading.CancellationToken>());
    }

    private static (NotificationDeliveryProcessor Processor, InMemoryNotificationDeliveryStore Store) CreateProcessor(
        DateTime now,
        INotificationNotifier[] notifiers,
        NotificationDeliveryOptions? options = null)
    {
        options ??= DeliveryOptions();
        var optionWrapper = Options.Create(options);
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(_ => now);
        var store = new InMemoryNotificationDeliveryStore();
        return (new NotificationDeliveryProcessor(
            store,
            notifiers,
            new NotificationDeliveryRetryPolicy(optionWrapper),
            clock,
            new TestCurrentTenant(),
            optionWrapper,
            NullLogger<NotificationDeliveryProcessor>.Instance), store);
    }

    private static NotificationDeliveryOptions DeliveryOptions(int maxAttempts = 3)
    {
        return new NotificationDeliveryOptions
        {
            MaxDeliveryAttempts = maxAttempts,
            DeliveryLeaseDuration = TimeSpan.FromMinutes(2),
            InitialDeliveryRetryDelay = TimeSpan.Zero,
            MaxDeliveryRetryDelay = TimeSpan.Zero,
            DeliveryRetryJitterFactor = 0
        };
    }

    private static NotificationDeliveryRequestedEto CreateWork(
        Guid notificationId,
        Guid userId,
        string channel)
    {
        return new NotificationDeliveryRequestedEto
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

    private sealed class TestReliableNotifier : INotificationNotifier
    {
        private readonly Func<NotificationDeliveryRequestedEto, CancellationToken, Task<NotificationDeliveryResult>> _deliver;

        public string Name { get; }

        public TestReliableNotifier(
            string name,
            Func<NotificationDeliveryRequestedEto, Task<NotificationDeliveryResult>> deliver)
            : this(name, (request, _) => deliver(request))
        {
        }

        public TestReliableNotifier(
            string name,
            Func<NotificationDeliveryRequestedEto, CancellationToken, Task<NotificationDeliveryResult>> deliver)
        {
            Name = name;
            _deliver = deliver;
        }

        public Task<NotificationDeliveryResult> DeliverAsync(
            NotificationDeliveryRequestedEto workItem,
            CancellationToken cancellationToken = default)
        {
            return _deliver(workItem, cancellationToken);
        }
    }

    private sealed class OtherTestNotifier : INotificationNotifier
    {
        public string Name { get; }

        public OtherTestNotifier(string name)
        {
            Name = name;
        }

        public Task<NotificationDeliveryResult> DeliverAsync(
            NotificationDeliveryRequestedEto request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(NotificationDeliveryResult.Succeeded());
    }
}
