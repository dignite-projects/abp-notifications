using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Provides the startup-validated notification definitions. Name registration and lookup use ordinal,
/// case-sensitive comparison.
/// </summary>
public interface INotificationDefinitionManager
{
    /// <summary>Gets a definition by its ordinal, case-sensitive name.</summary>
    NotificationDefinition Get(string name);

    /// <summary>Gets a definition by its ordinal, case-sensitive name, or <see langword="null"/>.</summary>
    NotificationDefinition? GetOrNull(string name);

    IReadOnlyList<NotificationDefinition> GetAll();

    Task<bool> IsAvailableAsync(string name, Guid userId);

    Task<IReadOnlyList<NotificationDefinition>> GetAllAvailableAsync(Guid userId);
}
