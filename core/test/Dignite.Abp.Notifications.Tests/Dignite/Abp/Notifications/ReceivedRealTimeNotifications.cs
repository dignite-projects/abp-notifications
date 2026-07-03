using System.Collections.Concurrent;

namespace Dignite.Abp.Notifications;

/// <summary>Singleton sink the test event handler appends delivered ETOs to.</summary>
public class ReceivedRealTimeNotifications
{
    public ConcurrentQueue<RealTimeNotifyEto> Items { get; } = new();
}
