namespace Dignite.Abp.Notifications;

/// <summary>The durable lifecycle of one notification recipient/channel delivery.</summary>
public enum NotificationDeliveryState
{
    Pending = 0,
    Claimed = 1,
    Succeeded = 2,
    Failed = 3,
    Suppressed = 4,
    DeadLetter = 5
}
