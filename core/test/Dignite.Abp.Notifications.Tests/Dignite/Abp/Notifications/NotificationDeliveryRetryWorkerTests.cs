using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryRetryWorkerTests : DigniteAbpNotificationsTestBase
{
    [Fact]
    public void Worker_uses_the_ABP_periodic_lifecycle()
    {
        var worker = GetRequiredService<NotificationDeliveryRetryWorker>();

        worker.ShouldBeAssignableTo<IBackgroundWorker>();
        worker.Period.ShouldBe((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        GetRequiredService<IAbpDistributedLock>().ShouldNotBeNull();
    }

    [Fact]
    public void Invalid_retry_worker_lock_configuration_is_rejected()
    {
        var options = new NotificationDeliveryOptions
        {
            DeliveryRetryWorkerLockName = " ",
            DeliveryRetryWorkerLockTimeout = TimeSpan.FromSeconds(-1)
        };

        var exception = Should.Throw<InvalidOperationException>(() => options.Validate());
        exception.Message.ShouldContain(nameof(NotificationDeliveryOptions.DeliveryRetryWorkerLockName));
    }

    [Fact]
    public async Task Two_competing_cycles_publish_from_only_one_scanner()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<IReadOnlyList<NotificationDeliveryRequestedEto>> WaitForReleaseAsync(
            CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Array.Empty<NotificationDeliveryRequestedEto>();
        }

        var store = Substitute.For<INotificationDeliveryStore>();
        store.GetDueWorkItemsAsync(
                Arg.Any<DateTime>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => WaitForReleaseAsync(callInfo.ArgAt<CancellationToken>(2)));
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(new NotificationDeliveryOptions(), distributedLock, store);

        var first = fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);
        release.TrySetResult();
        await first;

        await store.Received(1).GetDueWorkItemsAsync(
            Arg.Any<DateTime>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        distributedLock.AcquisitionCount.ShouldBe(2);
        distributedLock.AcquiredCount.ShouldBe(1);
    }

    [Fact]
    public async Task Disabled_worker_does_not_acquire_the_lock_or_scan()
    {
        var options = new NotificationDeliveryOptions { IsDeliveryRetryWorkerEnabled = false };
        var distributedLock = new TestDistributedLock();
        var store = Substitute.For<INotificationDeliveryStore>();
        using var fixture = CreateFixture(options, distributedLock, store);

        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        distributedLock.AcquisitionCount.ShouldBe(0);
        await store.DidNotReceiveWithAnyArgs().GetDueWorkItemsAsync(
            default,
            default,
            default);
    }

    [Fact]
    public async Task Lock_miss_skips_the_cycle_without_scanning()
    {
        var distributedLock = new TestDistributedLock(alwaysMiss: true);
        var store = Substitute.For<INotificationDeliveryStore>();
        using var fixture = CreateFixture(new NotificationDeliveryOptions(), distributedLock, store);

        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        distributedLock.AcquisitionCount.ShouldBe(1);
        await store.DidNotReceiveWithAnyArgs().GetDueWorkItemsAsync(
            default,
            default,
            default);
    }

    [Fact]
    public async Task Shutdown_cancellation_interrupts_event_publication_and_releases_the_lock()
    {
        var publicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task WaitForPublicationReleaseAsync()
        {
            publicationStarted.TrySetResult();
            await releasePublication.Task;
        }

        var workItem = new NotificationDeliveryRequestedEto();
        var store = Substitute.For<INotificationDeliveryStore>();
        store.GetDueWorkItemsAsync(
                Arg.Any<DateTime>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { workItem });
        var eventBus = Substitute.For<IDistributedEventBus>();
        eventBus.PublishAsync(
                Arg.Any<NotificationDeliveryRequestedEto>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
            .Returns(_ => WaitForPublicationReleaseAsync());
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(
            new NotificationDeliveryOptions(),
            distributedLock,
            store,
            eventBus);
        using var cancellationTokenSource = new CancellationTokenSource();

        var cycle = fixture.Worker.ExecuteCycleAsync(
            fixture.ServiceProvider,
            cancellationTokenSource.Token);
        await publicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationTokenSource.Cancel();
        await cycle.WaitAsync(TimeSpan.FromSeconds(5));
        releasePublication.TrySetResult();

        distributedLock.IsHeld.ShouldBeFalse();
        distributedLock.LastCancellationToken.ShouldBe(cancellationTokenSource.Token);
        await store.Received(1).GetDueWorkItemsAsync(
            Arg.Any<DateTime>(),
            Arg.Any<int>(),
            cancellationTokenSource.Token);
    }

    [Fact]
    public async Task A_failed_cycle_releases_the_lock_and_the_next_cycle_can_continue()
    {
        var attempt = 0;
        var store = Substitute.For<INotificationDeliveryStore>();
        store.GetDueWorkItemsAsync(
                Arg.Any<DateTime>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref attempt) == 1
                ? Task.FromException<IReadOnlyList<NotificationDeliveryRequestedEto>>(
                    new InvalidOperationException("scan failed"))
                : Task.FromResult<IReadOnlyList<NotificationDeliveryRequestedEto>>(
                    Array.Empty<NotificationDeliveryRequestedEto>()));
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(new NotificationDeliveryOptions(), distributedLock, store);

        await Should.ThrowAsync<InvalidOperationException>(
            () => fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider));
        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        attempt.ShouldBe(2);
        distributedLock.AcquiredCount.ShouldBe(2);
        distributedLock.IsHeld.ShouldBeFalse();
    }

    private static WorkerFixture CreateFixture(
        NotificationDeliveryOptions options,
        IAbpDistributedLock distributedLock,
        INotificationDeliveryStore store,
        IDistributedEventBus? eventBus = null)
    {
        var clock = Substitute.For<IClock>();
        clock.Now.Returns(DateTime.UtcNow);
        var services = new ServiceCollection();
        services.AddSingleton(distributedLock);
        services.AddSingleton(store);
        services.AddSingleton(clock);
        services.AddSingleton(eventBus ?? Substitute.For<IDistributedEventBus>());
        var serviceProvider = services.BuildServiceProvider();
        var worker = new NotificationDeliveryRetryWorker(
            new AbpAsyncTimer(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider,
            new AbpLazyServiceProvider(serviceProvider),
            Options.Create(options),
            NullLogger<NotificationDeliveryRetryWorker>.Instance);
        return new WorkerFixture(serviceProvider, worker);
    }

    private sealed class WorkerFixture : IDisposable
    {
        public ServiceProvider ServiceProvider { get; }
        public NotificationDeliveryRetryWorker Worker { get; }

        public WorkerFixture(ServiceProvider serviceProvider, NotificationDeliveryRetryWorker worker)
        {
            ServiceProvider = serviceProvider;
            Worker = worker;
        }

        public void Dispose()
        {
            ServiceProvider.Dispose();
        }
    }

    private sealed class TestDistributedLock : IAbpDistributedLock
    {
        private readonly bool _alwaysMiss;
        private int _isHeld;

        public int AcquisitionCount { get; private set; }
        public int AcquiredCount { get; private set; }
        public bool IsHeld => Volatile.Read(ref _isHeld) == 1;
        public CancellationToken LastCancellationToken { get; private set; }

        public TestDistributedLock(bool alwaysMiss = false)
        {
            _alwaysMiss = alwaysMiss;
        }

        public Task<IAbpDistributedLockHandle?> TryAcquireAsync(
            string name,
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquisitionCount++;
            LastCancellationToken = cancellationToken;
            if (_alwaysMiss || Interlocked.CompareExchange(ref _isHeld, 1, 0) != 0)
            {
                return Task.FromResult<IAbpDistributedLockHandle?>(null);
            }

            AcquiredCount++;
            return Task.FromResult<IAbpDistributedLockHandle?>(new TestHandle(
                () => Volatile.Write(ref _isHeld, 0)));
        }
    }

    private sealed class TestHandle : IAbpDistributedLockHandle
    {
        private readonly Action _release;
        private int _isDisposed;

        public TestHandle(Action release)
        {
            _release = release;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
