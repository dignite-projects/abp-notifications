namespace Dignite.Abp.NotificationCenter;

/// <summary>Persisted Notification Center record kinds governed by retention cleanup.</summary>
public enum NotificationRetentionRecordKind
{
    Notification = 0,

    UserNotification = 1,

    NotificationDelivery = 2
}
