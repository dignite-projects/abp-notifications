using System;
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
using Volo.Abp.Modularity;
using Volo.Abp.Threading;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

public abstract class NotificationPeriodicWorker_Tests<TStartupModule> :
    NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public void Retention_worker_uses_the_ABP_periodic_lifecycle()
    {
        var worker = GetRequiredService<NotificationRetentionCleanupWorker>();

        worker.ShouldBeAssignableTo<IBackgroundWorker>();
        worker.Period.ShouldBe((int)TimeSpan.FromHours(1).TotalMilliseconds);
    }

    [Fact]
    public void Invalid_retention_worker_lock_configuration_is_rejected()
    {
        var options = new NotificationRetentionOptions
        {
            CleanupWorkerLockName = " ",
            CleanupWorkerLockTimeout = TimeSpan.FromSeconds(-1)
        };

        var exception = Should.Throw<InvalidOperationException>(() => options.Validate());
        exception.Message.ShouldContain(nameof(NotificationRetentionOptions.CleanupWorkerLockName));
    }

    [Fact]
    public async Task Two_competing_retention_cycles_run_only_one_cleanup()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<NotificationRetentionCleanupResult> WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new NotificationRetentionCleanupResult();
        }

        var cleanupService = Substitute.For<INotificationRetentionCleanupService>();
        cleanupService.CleanupAsync(
                Arg.Any<NotificationRetentionCleanupRequest?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => WaitForReleaseAsync(callInfo.ArgAt<CancellationToken>(1)));
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            cleanupService);

        var first = fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);
        release.TrySetResult();
        await first;

        await cleanupService.Received(1).CleanupAsync(
            Arg.Any<NotificationRetentionCleanupRequest?>(),
            Arg.Any<CancellationToken>());
        distributedLock.AcquisitionCount.ShouldBe(2);
        distributedLock.AcquiredCount.ShouldBe(1);
    }

    [Fact]
    public async Task Disabled_retention_worker_does_not_acquire_the_lock_or_scan()
    {
        var distributedLock = new TestDistributedLock();
        var cleanupService = Substitute.For<INotificationRetentionCleanupService>();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = false },
            distributedLock,
            cleanupService);

        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        distributedLock.AcquisitionCount.ShouldBe(0);
        await cleanupService.DidNotReceiveWithAnyArgs().CleanupAsync(default, default);
    }

    [Fact]
    public async Task Retention_lock_miss_skips_without_running_cleanup()
    {
        var distributedLock = new TestDistributedLock(alwaysMiss: true);
        var cleanupService = Substitute.For<INotificationRetentionCleanupService>();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            cleanupService);

        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        distributedLock.AcquisitionCount.ShouldBe(1);
        await cleanupService.DidNotReceiveWithAnyArgs().CleanupAsync(default, default);
    }

    [Fact]
    public async Task Shutdown_cancellation_is_forwarded_to_retention_cleanup()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<NotificationRetentionCleanupResult> WaitForCancellationAsync(
            CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new NotificationRetentionCleanupResult();
        }

        var cleanupService = Substitute.For<INotificationRetentionCleanupService>();
        cleanupService.CleanupAsync(
                Arg.Any<NotificationRetentionCleanupRequest?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => WaitForCancellationAsync(callInfo.ArgAt<CancellationToken>(1)));
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            cleanupService);
        using var cancellationTokenSource = new CancellationTokenSource();

        var cycle = fixture.Worker.ExecuteCycleAsync(
            fixture.ServiceProvider,
            cancellationTokenSource.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationTokenSource.Cancel();
        await cycle.WaitAsync(TimeSpan.FromSeconds(5));

        distributedLock.IsHeld.ShouldBeFalse();
        distributedLock.LastCancellationToken.ShouldBe(cancellationTokenSource.Token);
        await cleanupService.Received(1).CleanupAsync(null, cancellationTokenSource.Token);
    }

    [Fact]
    public async Task Failed_retention_cycle_releases_the_lock_and_next_cycle_continues()
    {
        var attempt = 0;
        var cleanupService = Substitute.For<INotificationRetentionCleanupService>();
        cleanupService.CleanupAsync(
                Arg.Any<NotificationRetentionCleanupRequest?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref attempt) == 1
                ? Task.FromException<NotificationRetentionCleanupResult>(
                    new InvalidOperationException("cleanup failed"))
                : Task.FromResult(new NotificationRetentionCleanupResult()));
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            cleanupService);

        await Should.ThrowAsync<InvalidOperationException>(
            () => fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider));
        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        attempt.ShouldBe(2);
        distributedLock.AcquiredCount.ShouldBe(2);
        distributedLock.IsHeld.ShouldBeFalse();
    }

    private static WorkerFixture CreateFixture(
        NotificationRetentionOptions options,
        IAbpDistributedLock distributedLock,
        INotificationRetentionCleanupService cleanupService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(distributedLock);
        services.AddSingleton(cleanupService);
        var serviceProvider = services.BuildServiceProvider();
        var worker = new NotificationRetentionCleanupWorker(
            new AbpAsyncTimer(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider,
            new AbpLazyServiceProvider(serviceProvider),
            Options.Create(options),
            NullLogger<NotificationRetentionCleanupWorker>.Instance);
        return new WorkerFixture(serviceProvider, worker);
    }

    private sealed class WorkerFixture : IDisposable
    {
        public ServiceProvider ServiceProvider { get; }
        public NotificationRetentionCleanupWorker Worker { get; }

        public WorkerFixture(ServiceProvider serviceProvider, NotificationRetentionCleanupWorker worker)
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
