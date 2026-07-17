namespace Dignite.Abp.Notifications;

/// <summary>
/// Reads durable/untrusted payload JSON without letting one unsupported item fail a batch. Trusted boundaries can
/// continue using strict <see cref="INotificationDataSerializer.Deserialize"/>.
/// </summary>
public interface INotificationDataTolerantReader
{
    NotificationData? DeserializeTolerantly(string? json);
}
