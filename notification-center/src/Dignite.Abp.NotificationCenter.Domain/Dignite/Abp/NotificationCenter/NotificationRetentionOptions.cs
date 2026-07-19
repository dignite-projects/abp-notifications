using System;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Configures Notification Center retention cleanup. Cleanup is disabled by default.</summary>
public class NotificationRetentionOptions
{
    public const int MaxCleanupBatchSize = 10_000;

    public const string DefaultCleanupWorkerLockName =
        "Dignite.Abp.NotificationCenter:RetentionCleanupWorker";

    /// <summary>
    /// Enables the ABP periodic cleanup worker. Manual <see cref="NotificationRetentionManager"/> calls are
    /// always explicit and do not require this flag.
    /// </summary>
    public bool IsCleanupEnabled { get; set; }

    /// <summary>Delay between periodic cleanup scans when <see cref="IsCleanupEnabled"/> is true.</summary>
    public TimeSpan CleanupWorkerPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Distributed lock name used to serialize cleanup scans across instances.</summary>
    public string CleanupWorkerLockName { get; set; } = DefaultCleanupWorkerLockName;

    /// <summary>Maximum time one cleanup cycle waits for the distributed lock. The default skips immediately.</summary>
    public TimeSpan CleanupWorkerLockTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Maximum rows deleted per record kind in one cleanup batch.</summary>
    public int CleanupBatchSize { get; set; } = 100;

    /// <summary>
    /// Read inbox rows older than this value are eligible for deletion. Unread rows are always retained.
    /// Set to <c>null</c> to disable inbox cleanup.
    /// </summary>
    public TimeSpan? ReadUserNotificationRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Notification payload rows older than this value are deleted once no inbox row references them.
    /// Set to <c>null</c> to disable payload cleanup.
    /// </summary>
    public TimeSpan? OrphanNotificationRetention { get; set; } = TimeSpan.FromDays(90);

    internal void Validate()
    {
        if (CleanupWorkerPeriod <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(CleanupWorkerPeriod)} must be greater than zero.");
        }

        if (CleanupWorkerPeriod.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(CleanupWorkerPeriod)} must not exceed {int.MaxValue} milliseconds.");
        }

        if (CleanupBatchSize < 1 || CleanupBatchSize > MaxCleanupBatchSize)
        {
            throw new InvalidOperationException(
                $"{nameof(CleanupBatchSize)} must be between 1 and {MaxCleanupBatchSize}.");
        }

        if (string.IsNullOrWhiteSpace(CleanupWorkerLockName))
        {
            throw new InvalidOperationException($"{nameof(CleanupWorkerLockName)} must not be empty or whitespace.");
        }

        if (CleanupWorkerLockTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(CleanupWorkerLockTimeout)} must be greater than or equal to zero.");
        }

        ValidateRetention(ReadUserNotificationRetention, nameof(ReadUserNotificationRetention));
        ValidateRetention(OrphanNotificationRetention, nameof(OrphanNotificationRetention));
    }

    private static void ValidateRetention(TimeSpan? value, string propertyName)
    {
        if (value.HasValue && value.Value < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{propertyName} must be null or greater than or equal to zero.");
        }
    }
}
