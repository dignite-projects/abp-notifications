namespace Dignite.Abp.Notifications;

/// <summary>
/// Serializes <see cref="NotificationData"/> to/from JSON using stable discriminators.
/// <see cref="Deserialize"/> is deliberately strict for trusted/corruption-sensitive boundaries. Durable inbox
/// reads use the additive <see cref="INotificationDataTolerantReader"/> contract.
/// </summary>
public interface INotificationDataSerializer
{
    string? Serialize(NotificationData? data);

    NotificationData? Deserialize(string? json);
}
