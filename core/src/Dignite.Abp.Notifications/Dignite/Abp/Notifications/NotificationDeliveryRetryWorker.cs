using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

internal sealed class NotificationDeliveryRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<NotificationDeliveryOptions> _options;
    private readonly ILogger<NotificationDeliveryRetryWorker> _logger;

    public NotificationDeliveryRetryWorker(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<NotificationDeliveryOptions> options,
        ILogger<NotificationDeliveryRetryWorker> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Value.DeliveryRetryWorkerPeriod, stoppingToken);
                if (!_options.Value.IsDeliveryRetryWorkerEnabled)
                {
                    continue;
                }

                await PublishDueWorkItemsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The notification delivery retry scan failed.");
            }
        }
    }

    private async Task PublishDueWorkItemsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationDeliveryStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IDistributedEventBus>();
        var workItems = await store.GetDueWorkItemsAsync(
            clock.Now,
            _options.Value.DeliveryRetryBatchSize,
            cancellationToken);

        foreach (var workItem in workItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await eventBus.PublishAsync(workItem);
            NotificationDeliveryMetrics.RetryPublishedCount.Add(1);
        }
    }
}
