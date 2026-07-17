using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Materializes notification definitions in the host's starting phase so registration conflicts are reported
/// after dependency injection and options construction, but before any hosted service can publish notifications.
/// </summary>
internal sealed class NotificationDefinitionStartupService : IHostedLifecycleService
{
    private readonly INotificationDefinitionManager _definitionManager;

    public NotificationDefinitionStartupService(INotificationDefinitionManager definitionManager)
    {
        _definitionManager = definitionManager;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _definitionManager.GetAll();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
