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
    private readonly INotificationDataTypeRegistry _dataTypeRegistry;

    public NotificationDefinitionStartupService(
        INotificationDefinitionManager definitionManager,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        _definitionManager = definitionManager;
        _dataTypeRegistry = dataTypeRegistry;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in _definitionManager.GetAll())
        {
            NotificationDefinitionContractValidator.ValidateRegistration(definition, _dataTypeRegistry);
        }
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
