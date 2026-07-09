using System.Collections.Concurrent;

namespace Dignite.Abp.Notifications;

/// <summary>Singleton sink the test event handler appends delivered ETOs to.</summary>
public class ReceivedNotificationDeliveries
{
    public ConcurrentQueue<NotificationDeliveryEto> Items { get; } = new();
}
