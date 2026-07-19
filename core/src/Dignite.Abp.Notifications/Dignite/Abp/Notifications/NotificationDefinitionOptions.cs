using System;
using Volo.Abp.Collections;

namespace Dignite.Abp.Notifications;

/// <summary>Configures notification definition discovery independently from runtime delivery tuning.</summary>
public class NotificationDefinitionOptions
{
    /// <summary>
    /// Gets definition provider types discovered across application modules or registered explicitly. Repeating the
    /// same provider type is idempotent; the definition manager executes each provider type once.
    /// </summary>
    public ITypeList<INotificationDefinitionProvider> DefinitionProviders { get; }

    public NotificationDefinitionOptions()
    {
        DefinitionProviders = new TypeList<INotificationDefinitionProvider>();
    }

    internal void Validate()
    {
        foreach (var providerType in DefinitionProviders)
        {
            if (!typeof(INotificationDefinitionProvider).IsAssignableFrom(providerType))
            {
                throw new InvalidOperationException(
                    $"{nameof(NotificationDefinitionOptions)}.{nameof(DefinitionProviders)} contains " +
                    $"'{providerType.FullName}', which is not a notification definition provider.");
            }
        }
    }
}
