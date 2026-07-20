using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Materializes notification definitions (and the data-type registry, via constructor injection) in the host's
/// starting phase so registration conflicts are reported at startup, before any hosted service can publish
/// notifications.
/// </summary>
internal sealed class NotificationDefinitionStartupService : IHostedLifecycleService
{
    private readonly INotificationDefinitionManager _definitionManager;

    public NotificationDefinitionStartupService(
        INotificationDefinitionManager definitionManager,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        _definitionManager = definitionManager;
        _ = dataTypeRegistry;
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
