namespace Dignite.Abp.Notifications;

/// <summary>Stable constants for the notification payload envelope.</summary>
public static class NotificationDataSchema
{
    /// <summary>
    /// Schema assigned to payload JSON written before an explicit <c>schemaVersion</c> member existed.
    /// </summary>
    public const int LegacyVersion = 1;
}
