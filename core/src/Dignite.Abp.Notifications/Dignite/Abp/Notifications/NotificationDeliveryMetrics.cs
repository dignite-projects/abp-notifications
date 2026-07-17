using System.Diagnostics.Metrics;

namespace Dignite.Abp.Notifications;

internal static class NotificationDeliveryMetrics
{
    private static readonly Meter Meter = new Meter("Dignite.Abp.Notifications.Delivery", "1.0.0");

    public static readonly Counter<long> ClaimCount = Meter.CreateCounter<long>("notification.delivery.claims");

    public static readonly Counter<long> OutcomeCount = Meter.CreateCounter<long>("notification.delivery.outcomes");

    public static readonly Counter<long> RetryPublishedCount =
        Meter.CreateCounter<long>("notification.delivery.retry.work_items");
}
