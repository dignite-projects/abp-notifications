using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Threading;

namespace Dignite.Abp.NotificationCenter;

internal class NotificationRetentionCleanupWorker : AsyncPeriodicBackgroundWorkerBase
{
    private readonly IOptions<NotificationRetentionOptions> _options;
    private readonly ILogger<NotificationRetentionCleanupWorker> _logger;

    public NotificationRetentionCleanupWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IServiceProvider serviceProvider,
        IAbpLazyServiceProvider lazyServiceProvider,
        IOptions<NotificationRetentionOptions> options,
        ILogger<NotificationRetentionCleanupWorker> logger)
        : base(timer, serviceScopeFactory)
    {
        _options = options;
        _logger = logger;
        ServiceProvider = serviceProvider;
        LazyServiceProvider = lazyServiceProvider;
        Timer.Period = checked((int)options.Value.CleanupWorkerPeriod.TotalMilliseconds);
    }

    protected override Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        return ExecuteCycleAsync(workerContext.ServiceProvider, workerContext.CancellationToken);
    }

    internal async Task ExecuteCycleAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.IsCleanupEnabled)
        {
            return;
        }

        try
        {
            var distributedLock = serviceProvider.GetRequiredService<IAbpDistributedLock>();
            await using var handle = await distributedLock.TryAcquireAsync(
                _options.Value.CleanupWorkerLockName,
                _options.Value.CleanupWorkerLockTimeout,
                cancellationToken);
            if (handle == null)
            {
                NotificationRetentionMetrics.RecordWorkerCycle("lock_miss");
                _logger.LogDebug(
                    "Skipped the notification retention cleanup scan because lock {LockName} is held by another worker.",
                    _options.Value.CleanupWorkerLockName);
                return;
            }

            var result = await CleanupAsync(serviceProvider, cancellationToken);

            NotificationRetentionMetrics.RecordWorkerCycle("completed");
            if (result.ScannedCount > 0 || result.ErrorCount > 0)
            {
                _logger.LogInformation(
                    "Notification retention cleanup scanned {ScannedCount}, deleted {DeletedCount}, skipped {SkippedCount}, errors {ErrorCount}.",
                    result.ScannedCount,
                    result.DeletedCount,
                    result.SkippedCount,
                    result.ErrorCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("The notification retention cleanup scan was canceled during shutdown.");
        }
        catch
        {
            NotificationRetentionMetrics.RecordWorkerCycle("failed");
            throw;
        }
    }

    protected virtual Task<NotificationRetentionCleanupResult> CleanupAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return serviceProvider.GetRequiredService<NotificationRetentionManager>()
            .CleanupAsync(cancellationToken: cancellationToken);
    }
}
