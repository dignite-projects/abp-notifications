using System;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Configures Notification Center retention cleanup. Cleanup is disabled by default.</summary>
public class NotificationRetentionOptions
{
    public const int MaxCleanupBatchSize = 10_000;

    /// <summary>
    /// Enables the hosted cleanup worker. Manual <see cref="INotificationRetentionCleanupService"/> calls are
    /// always explicit and do not require this flag.
    /// </summary>
    public bool IsCleanupEnabled { get; set; }

    /// <summary>Delay between hosted cleanup scans when <see cref="IsCleanupEnabled"/> is true.</summary>
    public TimeSpan CleanupWorkerPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Maximum candidates scanned per retained record kind in one cleanup pass.</summary>
    public int CleanupBatchSize { get; set; } = 100;

    /// <summary>
    /// Read inbox rows older than this value are eligible for deletion. Unread rows are always retained.
    /// Set to <c>null</c> to disable time-based inbox cleanup.
    /// </summary>
    public TimeSpan? ReadUserNotificationRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Terminal delivery records older than this value are eligible for deletion. Pending, failed retry, and
    /// claimed records are always retained.
    /// </summary>
    public TimeSpan? TerminalDeliveryRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Base notification payload rows older than this value are eligible only after no retained inbox or delivery
    /// record in the same tenant references them.
    /// </summary>
    public TimeSpan? OrphanNotificationRetention { get; set; } = TimeSpan.FromDays(90);

    internal void Validate()
    {
        if (CleanupWorkerPeriod <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(CleanupWorkerPeriod)} must be greater than zero.");
        }

        if (CleanupBatchSize < 1 || CleanupBatchSize > MaxCleanupBatchSize)
        {
            throw new InvalidOperationException(
                $"{nameof(CleanupBatchSize)} must be between 1 and {MaxCleanupBatchSize}.");
        }

        ValidateRetention(ReadUserNotificationRetention, nameof(ReadUserNotificationRetention));
        ValidateRetention(TerminalDeliveryRetention, nameof(TerminalDeliveryRetention));
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
