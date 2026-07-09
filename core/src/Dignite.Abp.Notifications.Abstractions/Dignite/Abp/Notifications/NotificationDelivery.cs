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
            CreationTime = eto.CreationTime
        };
    }
}
