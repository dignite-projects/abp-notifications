namespace Dignite.Abp.Notifications;

/// <summary>
/// Serializes <see cref="NotificationData"/> to/from JSON using stable discriminators.
/// The single mechanism shared by persistence, the event bus and remote HTTP clients.
/// </summary>
public interface INotificationDataSerializer
{
    string? Serialize(NotificationData? data);

    NotificationData? Deserialize(string? json);
}
