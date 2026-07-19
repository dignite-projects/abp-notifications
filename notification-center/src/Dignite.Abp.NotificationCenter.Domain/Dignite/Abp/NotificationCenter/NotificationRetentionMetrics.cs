using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace Dignite.Abp.NotificationCenter;

public static class NotificationRetentionMetrics
{
    public const string MeterName = "Dignite.Abp.NotificationCenter.Retention";
    public const string ScannedCountName = "dignite.notifications.retention.scanned";
    public const string DeletedCountName = "dignite.notifications.retention.deleted";
    public const string SkippedCountName = "dignite.notifications.retention.skipped";
    public const string ErrorCountName = "dignite.notifications.retention.errors";
    public const string OldestRetainedUnixTimeName = "dignite.notifications.retention.oldest_retained_unix_ms";
    public const string WorkerCycleCountName = "dignite.notifications.retention.worker.cycles";

    internal static readonly Meter Meter = new(MeterName);

    private static long _oldestNotificationUnixTimeMs = -1;
    private static long _oldestUserNotificationUnixTimeMs = -1;
    private static long _oldestDeliveryUnixTimeMs = -1;

    internal static readonly Counter<long> ScannedCount = Meter.CreateCounter<long>(ScannedCountName);

    internal static readonly Counter<long> DeletedCount = Meter.CreateCounter<long>(DeletedCountName);

    internal static readonly Counter<long> SkippedCount = Meter.CreateCounter<long>(SkippedCountName);

    internal static readonly Counter<long> ErrorCount = Meter.CreateCounter<long>(ErrorCountName);

    internal static readonly Counter<long> WorkerCycleCount = Meter.CreateCounter<long>(WorkerCycleCountName);

    private static readonly ObservableGauge<long> OldestRetainedUnixTime = Meter.CreateObservableGauge(
        OldestRetainedUnixTimeName,
        ObserveOldestRetainedUnixTime,
        "ms",
        "Oldest retained record creation timestamp, expressed as Unix time in milliseconds.");

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

    internal static void RecordOldestRetained(
        DateTime? oldestNotificationCreationTime,
        DateTime? oldestUserNotificationCreationTime,
        DateTime? oldestDeliveryCreationTime)
    {
        SetUnixTime(ref _oldestNotificationUnixTimeMs, oldestNotificationCreationTime);
        SetUnixTime(ref _oldestUserNotificationUnixTimeMs, oldestUserNotificationCreationTime);
        SetUnixTime(ref _oldestDeliveryUnixTimeMs, oldestDeliveryCreationTime);
    }

    internal static void RecordWorkerCycle(string outcome)
    {
        var tags = new TagList { { "outcome", outcome } };
        WorkerCycleCount.Add(1, tags);
    }

    private static Measurement<long>[] ObserveOldestRetainedUnixTime()
    {
        return new[]
        {
            CreateOldestMeasurement("notification", Volatile.Read(ref _oldestNotificationUnixTimeMs)),
            CreateOldestMeasurement("user_notification", Volatile.Read(ref _oldestUserNotificationUnixTimeMs)),
            CreateOldestMeasurement("notification_delivery", Volatile.Read(ref _oldestDeliveryUnixTimeMs))
        }.Where(measurement => measurement.Value >= 0).ToArray();
    }

    private static Measurement<long> CreateOldestMeasurement(string recordKind, long value)
    {
        return new Measurement<long>(value, new KeyValuePair<string, object?>("record_kind", recordKind));
    }

    private static void SetUnixTime(ref long target, DateTime? value)
    {
        Interlocked.Exchange(
            ref target,
            value.HasValue ? ToUnixTimeMilliseconds(value.Value) : -1);
    }

    private static long ToUnixTimeMilliseconds(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }
}
