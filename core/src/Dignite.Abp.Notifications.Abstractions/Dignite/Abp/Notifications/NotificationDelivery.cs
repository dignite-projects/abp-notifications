using System;

namespace Dignite.Abp.Notifications;

/// <summary>
/// The per-user view of a notification pushed to a client (e.g. over SignalR). Deliberately omits the recipient
/// list carried by <see cref="NotificationDeliveryEto"/>, so a user never receives other users' ids.
/// </summary>
public class NotificationDelivery
{
    public Guid NotificationId { get; set; }

    public string NotificationName { get; set; } = default!;

    public NotificationData? Data { get; set; }

    public NotificationSeverity Severity { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Stable name of the entity type this notification is about, e.g. <c>"Demo.Order"</c> — never a CLR type name.
    /// Null when the notification is not about a specific entity.
    /// </summary>
    public string? EntityTypeName { get; set; }

    /// <summary>The entity's identifier, rendered as a string. Null when <see cref="EntityTypeName"/> is null.</summary>
    public string? EntityId { get; set; }

    public NotificationDelivery()
    {
    }

    public static NotificationDelivery FromEto(NotificationDeliveryEto eto)
    {
        return new NotificationDelivery
        {
            NotificationId = eto.NotificationId,
            NotificationName = eto.NotificationName,
            Data = eto.Data,
            Severity = eto.Severity,
            CreationTime = eto.CreationTime,
            EntityTypeName = eto.EntityTypeName,
            EntityId = eto.EntityId
        };
    }

    public static NotificationDelivery FromWorkItem(NotificationDeliveryRequestedEto workItem)
    {
        return new NotificationDelivery
        {
            NotificationId = workItem.NotificationId,
            NotificationName = workItem.NotificationName,
            Data = workItem.Data,
            Severity = workItem.Severity,
            CreationTime = workItem.CreationTime,
            EntityTypeName = workItem.EntityTypeName,
            EntityId = workItem.EntityId
        };
    }
}
