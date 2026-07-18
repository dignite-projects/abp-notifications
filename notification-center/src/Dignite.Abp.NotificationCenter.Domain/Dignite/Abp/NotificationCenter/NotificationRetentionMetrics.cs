using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Dignite.Abp.NotificationCenter;

public static class NotificationRetentionMetrics
{
    public const string MeterName = "Dignite.Abp.NotificationCenter.Retention";
    public const string ScannedCountName = "dignite.notifications.retention.scanned";
    public const string DeletedCountName = "dignite.notifications.retention.deleted";
    public const string SkippedCountName = "dignite.notifications.retention.skipped";
    public const string ErrorCountName = "dignite.notifications.retention.errors";

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> ScannedCount = Meter.CreateCounter<long>(ScannedCountName);

    internal static readonly Counter<long> DeletedCount = Meter.CreateCounter<long>(DeletedCountName);

    internal static readonly Counter<long> SkippedCount = Meter.CreateCounter<long>(SkippedCountName);

    internal static readonly Counter<long> ErrorCount = Meter.CreateCounter<long>(ErrorCountName);

    internal static void Record(string recordKind, bool isDryRun, long scanned, long deleted, long skipped, long errors)
    {
        var tags = new TagList
        {
            { "record_kind", recordKind },
            { "dry_run", isDryRun }
        };

        if (scanned > 0)
        {
            ScannedCount.Add(scanned, tags);
        }

        if (deleted > 0)
        {
            DeletedCount.Add(deleted, tags);
        }

        if (skipped > 0)
        {
            SkippedCount.Add(skipped, tags);
        }

        if (errors > 0)
        {
            ErrorCount.Add(errors, tags);
        }
    }
}
