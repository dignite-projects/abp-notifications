namespace Dignite.Abp.Notifications;

/// <summary>
/// Serializes <see cref="NotificationData"/> to/from JSON using stable discriminators.
/// </summary>
public interface INotificationDataSerializer
{
    string? Serialize(NotificationData? data);

    /// <summary>Reads JSON using the explicitly selected strict or tolerant failure policy.</summary>
    NotificationData? Deserialize(string? json, NotificationDataReadMode readMode);
}
