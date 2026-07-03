using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

public interface INotificationDefinitionManager
{
    NotificationDefinition Get(string name);

    NotificationDefinition? GetOrNull(string name);

    IReadOnlyList<NotificationDefinition> GetAll();

    Task<bool> IsAvailableAsync(string name, Guid userId);

    Task<IReadOnlyList<NotificationDefinition>> GetAllAvailableAsync(Guid userId);
}
