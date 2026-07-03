using Dignite.Abp.Notifications;

namespace Dignite.Abp.NotificationCenter;

/// <summary>A custom notification payload standing in for one a business module would define.</summary>
[NotificationDataType("Test.OrderShipped")]
public class OrderShippedNotificationData : NotificationData
{
    public string OrderNumber { get; set; } = default!;

    public int ItemCount { get; set; }
}
