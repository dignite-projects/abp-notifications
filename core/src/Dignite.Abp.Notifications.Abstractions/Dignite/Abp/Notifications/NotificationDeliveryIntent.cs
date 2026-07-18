namespace Dignite.Abp.Notifications;

/// <summary>
/// The producer-resolved intent for one recipient/channel delivery. A remote channel consumer must honor this
/// value without looking up user preferences in its own process.
/// </summary>
public enum NotificationDeliveryIntent
{
    Deliver = 0,
    Suppress = 1,
    Delay = 2
}
