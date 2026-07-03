using Volo.Abp.Collections;

namespace Dignite.Abp.Notifications;

public class NotificationOptions
{
    /// <summary>
    /// Maximum number of explicitly-targeted users a notification may have before it is distributed on a
    /// background job instead of inline. This was a hard-coded constant in the reference implementation;
    /// it is now configurable.
    /// </summary>
    public int MaxUserCountToDirectlyDistributeANotification { get; set; } = 5;

    public ITypeList<INotificationDefinitionProvider> DefinitionProviders { get; }

    public NotificationOptions()
    {
        DefinitionProviders = new TypeList<INotificationDefinitionProvider>();
    }
}
