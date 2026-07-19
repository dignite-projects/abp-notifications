using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Threading;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

internal sealed class NotificationDeliveryRetryWorker : AsyncPeriodicBackgroundWorkerBase
{
    private readonly IOptions<NotificationDeliveryOptions> _options;
    private readonly ILogger<NotificationDeliveryRetryWorker> _logger;

    public NotificationDeliveryRetryWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IServiceProvider serviceProvider,
        IAbpLazyServiceProvider lazyServiceProvider,
        IOptions<NotificationDeliveryOptions> options,
        ILogger<NotificationDeliveryRetryWorker> logger)
        : base(timer, serviceScopeFactory)
    {
        _options = options;
        _logger = logger;
        ServiceProvider = serviceProvider;
        LazyServiceProvider = lazyServiceProvider;
        Timer.Period = checked((int)options.Value.DeliveryRetryWorkerPeriod.TotalMilliseconds);
    }

    protected override Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        return ExecuteCycleAsync(workerContext.ServiceProvider, workerContext.CancellationToken);
    }

    internal async Task ExecuteCycleAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.IsDeliveryRetryWorkerEnabled)
        {
            return;
        }

        try
        {
            var distributedLock = serviceProvider.GetRequiredService<IAbpDistributedLock>();
            await using var handle = await distributedLock.TryAcquireAsync(
                _options.Value.DeliveryRetryWorkerLockName,
                _options.Value.DeliveryRetryWorkerLockTimeout,
                cancellationToken);
            if (handle == null)
            {
                NotificationDeliveryMetrics.RecordRetryScan("lock_miss");
                _logger.LogDebug(
                    "Skipped the notification delivery retry scan because lock {LockName} is held by another worker.",
                    _options.Value.DeliveryRetryWorkerLockName);
                return;
            }

            var store = serviceProvider.GetRequiredService<INotificationDeliveryStore>();
            var clock = serviceProvider.GetRequiredService<IClock>();
            var eventBus = serviceProvider.GetRequiredService<IDistributedEventBus>();
            var workItems = await store.GetDueWorkItemsAsync(
                clock.Now,
                _options.Value.DeliveryRetryBatchSize,
                cancellationToken);

            foreach (var workItem in workItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await eventBus.PublishAsync(workItem).WaitAsync(cancellationToken);
                NotificationDeliveryMetrics.RetryPublishedCount.Add(1);
            }

            NotificationDeliveryMetrics.RecordRetryScan("completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("The notification delivery retry scan was canceled during shutdown.");
        }
        catch
        {
            NotificationDeliveryMetrics.RecordRetryScan("failed");
            throw;
        }
    }
}
