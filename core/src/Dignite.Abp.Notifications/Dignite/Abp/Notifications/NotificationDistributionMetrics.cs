using System.Diagnostics.Metrics;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Stable meter and instrument names for observing bounded notification distribution. Counters describe attempted
/// pipeline work; provider transaction semantics determine whether that work was committed.
/// </summary>
public static class NotificationDistributionMetrics
{
    public const string MeterName = "Dignite.Abp.Notifications";
    public const string CandidateCountName = "dignite.notifications.distribution.candidates";
    public const string EligibleCountName = "dignite.notifications.distribution.eligible";
    public const string FilteredCountName = "dignite.notifications.distribution.filtered";
    public const string BatchCountName = "dignite.notifications.distribution.batches";
    public const string DurationName = "dignite.notifications.distribution.duration";
    public const string FailureCountName = "dignite.notifications.distribution.failures";

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> CandidateCount =
        Meter.CreateCounter<long>(CandidateCountName, "{recipient}");

    internal static readonly Counter<long> EligibleCount =
        Meter.CreateCounter<long>(EligibleCountName, "{recipient}");

    internal static readonly Counter<long> FilteredCount =
        Meter.CreateCounter<long>(FilteredCountName, "{recipient}");

    internal static readonly Counter<long> BatchCount =
        Meter.CreateCounter<long>(BatchCountName, "{batch}");

    internal static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>(DurationName, "ms");

    internal static readonly Counter<long> FailureCount =
        Meter.CreateCounter<long>(FailureCountName, "{failure}");
}
