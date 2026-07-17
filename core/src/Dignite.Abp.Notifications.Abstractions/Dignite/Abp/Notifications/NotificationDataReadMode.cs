namespace Dignite.Abp.Notifications;

/// <summary>Controls whether an unreadable payload throws or becomes an unsupported-data placeholder.</summary>
public enum NotificationDataReadMode
{
    /// <summary>Reject unknown, future, malformed, or non-upcastable payloads.</summary>
    Strict = 0,

    /// <summary>Return <see cref="UnsupportedNotificationData"/> so one payload cannot break a whole page/event.</summary>
    Tolerant = 1
}
