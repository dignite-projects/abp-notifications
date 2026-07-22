using System;

namespace Dignite.Abp.Notifications;

/// <summary>
/// The per-user view of a notification pushed to a client (e.g. over SignalR). It deliberately carries no
/// aggregate recipient list, so a user never receives other users' ids.
/// </summary>
public class NotificationPayload
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

    public NotificationPayload()
    {
    }

    public static NotificationPayload FromRequest(NotificationDeliveryRequestedEto request)
    {
        return new NotificationPayload
        {
            NotificationId = request.NotificationId,
            NotificationName = request.NotificationName,
            Data = request.Data,
            Severity = request.Severity,
            CreationTime = request.CreationTime,
            EntityTypeName = request.EntityTypeName,
            EntityId = request.EntityId
        };
    }
}
