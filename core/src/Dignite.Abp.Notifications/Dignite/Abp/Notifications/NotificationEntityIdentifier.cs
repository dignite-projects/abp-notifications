using System;

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
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        EntityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
    }
}
