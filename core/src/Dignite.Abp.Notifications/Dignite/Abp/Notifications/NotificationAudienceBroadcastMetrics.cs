using System.Diagnostics.Metrics;

namespace Dignite.Abp.Notifications;

public static class NotificationAudienceBroadcastMetrics
{
    public const string MeterName = "Dignite.Abp.Notifications.AudienceBroadcast";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> PageCount = Meter.CreateCounter<long>(
        "dignite.notifications.audience.broadcast.pages");

    public static readonly Counter<long> CandidateCount = Meter.CreateCounter<long>(
        "dignite.notifications.audience.broadcast.candidates");

    public static readonly Counter<long> FailureCount = Meter.CreateCounter<long>(
        "dignite.notifications.audience.broadcast.failures");
}
