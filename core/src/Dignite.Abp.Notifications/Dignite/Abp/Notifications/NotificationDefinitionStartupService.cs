using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Validates <see cref="NotificationDefinitionOptions"/> and materializes notification definitions (plus the
/// data-type registry, via constructor injection) in the host's starting phase, before any hosted service can
/// publish notifications — the single startup fail-fast hook for both concerns, since the real definition-name
/// conflict check only runs lazily inside <see cref="NotificationDefinitionManager"/> and can't be forced from
/// the options-validation pipeline without it resolving itself.
/// </summary>
internal sealed class NotificationDefinitionStartupService : IHostedLifecycleService
{
    private readonly INotificationDefinitionManager _definitionManager;
    private readonly IOptions<NotificationDefinitionOptions> _definitionOptions;

    public NotificationDefinitionStartupService(
        INotificationDefinitionManager definitionManager,
        IOptions<NotificationDefinitionOptions> definitionOptions,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        _definitionManager = definitionManager;
        _definitionOptions = definitionOptions;
        _ = dataTypeRegistry;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _definitionOptions.Value.Validate();
        _definitionManager.GetAll();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
