using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dignite.Abp.NotificationCenter;

internal sealed class NotificationRetentionCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<NotificationRetentionOptions> _options;
    private readonly ILogger<NotificationRetentionCleanupWorker> _logger;

    public NotificationRetentionCleanupWorker(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<NotificationRetentionOptions> options,
        ILogger<NotificationRetentionCleanupWorker> logger)
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
                await Task.Delay(_options.Value.CleanupWorkerPeriod, stoppingToken);
                if (!_options.Value.IsCleanupEnabled)
                {
                    continue;
                }

                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The notification retention cleanup scan failed.");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var cleanupService = scope.ServiceProvider.GetRequiredService<INotificationRetentionCleanupService>();
        var result = await cleanupService.CleanupAsync(cancellationToken: cancellationToken);

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
}
