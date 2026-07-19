using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

        var cleanupCount = 0;
        async Task<NotificationRetentionCleanupResult> CleanupAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref cleanupCount);
            return await WaitForReleaseAsync(cancellationToken);
        }
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            CleanupAsync);

        var first = fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);
        release.TrySetResult();
        await first;

        cleanupCount.ShouldBe(1);
        distributedLock.AcquisitionCount.ShouldBe(2);
        distributedLock.AcquiredCount.ShouldBe(1);
    }

    [Fact]
    public async Task Disabled_retention_worker_does_not_acquire_the_lock_or_scan()
    {
        var distributedLock = new TestDistributedLock();
        var cleanupCount = 0;
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = false },
            distributedLock,
            _ =>
            {
                Interlocked.Increment(ref cleanupCount);
                return Task.FromResult(new NotificationRetentionCleanupResult());
            });

        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        distributedLock.AcquisitionCount.ShouldBe(0);
        cleanupCount.ShouldBe(0);
    }

    [Fact]
    public async Task Retention_lock_miss_skips_without_running_cleanup()
    {
        var distributedLock = new TestDistributedLock(alwaysMiss: true);
        var cleanupCount = 0;
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            _ =>
            {
                Interlocked.Increment(ref cleanupCount);
                return Task.FromResult(new NotificationRetentionCleanupResult());
            });

        await fixture.Worker.ExecuteCycleAsync(fixture.ServiceProvider);

        distributedLock.AcquisitionCount.ShouldBe(1);
        cleanupCount.ShouldBe(0);
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

        var cleanupCount = 0;
        var receivedToken = default(CancellationToken);
        async Task<NotificationRetentionCleanupResult> CleanupAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref cleanupCount);
            receivedToken = cancellationToken;
            return await WaitForCancellationAsync(cancellationToken);
        }
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            CleanupAsync);
        using var cancellationTokenSource = new CancellationTokenSource();

        var cycle = fixture.Worker.ExecuteCycleAsync(
            fixture.ServiceProvider,
            cancellationTokenSource.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationTokenSource.Cancel();
        await cycle.WaitAsync(TimeSpan.FromSeconds(5));

        distributedLock.IsHeld.ShouldBeFalse();
        distributedLock.LastCancellationToken.ShouldBe(cancellationTokenSource.Token);
        cleanupCount.ShouldBe(1);
        receivedToken.ShouldBe(cancellationTokenSource.Token);
    }

    [Fact]
    public async Task Failed_retention_cycle_releases_the_lock_and_next_cycle_continues()
    {
        var attempt = 0;
        Task<NotificationRetentionCleanupResult> CleanupAsync(CancellationToken _)
        {
            return Interlocked.Increment(ref attempt) == 1
                ? Task.FromException<NotificationRetentionCleanupResult>(
                    new InvalidOperationException("cleanup failed"))
                : Task.FromResult(new NotificationRetentionCleanupResult());
        }
        var distributedLock = new TestDistributedLock();
        using var fixture = CreateFixture(
            new NotificationRetentionOptions { IsCleanupEnabled = true },
            distributedLock,
            CleanupAsync);

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
        Func<CancellationToken, Task<NotificationRetentionCleanupResult>> cleanupAsync)
    {
        var services = new ServiceCollection();
        services.AddSingleton(distributedLock);
        var serviceProvider = services.BuildServiceProvider();
        var worker = new TestNotificationRetentionCleanupWorker(
            new AbpAsyncTimer(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider,
            new AbpLazyServiceProvider(serviceProvider),
            Options.Create(options),
            NullLogger<NotificationRetentionCleanupWorker>.Instance,
            cleanupAsync);
        return new WorkerFixture(serviceProvider, worker);
    }

    private sealed class TestNotificationRetentionCleanupWorker : NotificationRetentionCleanupWorker
    {
        private readonly Func<CancellationToken, Task<NotificationRetentionCleanupResult>> _cleanupAsync;

        public TestNotificationRetentionCleanupWorker(
            AbpAsyncTimer timer,
            IServiceScopeFactory serviceScopeFactory,
            IServiceProvider serviceProvider,
            IAbpLazyServiceProvider lazyServiceProvider,
            IOptions<NotificationRetentionOptions> options,
            Microsoft.Extensions.Logging.ILogger<NotificationRetentionCleanupWorker> logger,
            Func<CancellationToken, Task<NotificationRetentionCleanupResult>> cleanupAsync)
            : base(timer, serviceScopeFactory, serviceProvider, lazyServiceProvider, options, logger)
        {
            _cleanupAsync = cleanupAsync;
        }

        protected override Task<NotificationRetentionCleanupResult> CleanupAsync(
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            return _cleanupAsync(cancellationToken);
        }
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
