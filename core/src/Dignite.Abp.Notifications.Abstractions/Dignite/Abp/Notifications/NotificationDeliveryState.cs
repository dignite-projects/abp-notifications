namespace Dignite.Abp.Notifications;

/// <summary>The durable lifecycle of one notification recipient/channel delivery.</summary>
public enum NotificationDeliveryState
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    RetryScheduled = 3,
    Suppressed = 4,
    DeadLettered = 5
}
