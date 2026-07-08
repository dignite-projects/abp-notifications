using System;
using Volo.Abp;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Identifies the entity a notification relates to (e.g. a specific order or post).
/// </summary>
public class NotificationEntityIdentifier
{
    public Type EntityType { get; }

    public object EntityId { get; }

    public NotificationEntityIdentifier(Type entityType, object entityId)
    {
        EntityType = Check.NotNull(entityType, nameof(entityType));
        EntityId = Check.NotNull(entityId, nameof(entityId));
    }
}
