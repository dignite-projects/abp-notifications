using System;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Configures Notification Center retention cleanup. Cleanup is disabled by default.</summary>
public class NotificationRetentionOptions
{
    public const int MaxCleanupBatchSize = 10_000;

    public const string DefaultCleanupWorkerLockName =
        "Dignite.Abp.NotificationCenter:RetentionCleanupWorker";

    /// <summary>
    /// Enables the ABP periodic cleanup worker. Manual <see cref="INotificationRetentionCleanupService"/> calls are
    /// always explicit and do not require this flag.
    /// </summary>
    public bool IsCleanupEnabled { get; set; }

    /// <summary>Delay between periodic cleanup scans when <see cref="IsCleanupEnabled"/> is true.</summary>
    public TimeSpan CleanupWorkerPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Distributed lock name used to serialize cleanup scans. Configure
    /// <c>AbpDistributedLockOptions.KeyPrefix</c> when applications share a lock provider but not notification data.
    /// </summary>
    public string CleanupWorkerLockName { get; set; } = DefaultCleanupWorkerLockName;

    /// <summary>Maximum time one cleanup cycle waits for the distributed lock. The default skips immediately.</summary>
    public TimeSpan CleanupWorkerLockTimeout { get; set; } = TimeSpan.Zero;

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
    /// Completed or canceled audience-broadcast workflow state older than this value is eligible for deletion.
    /// Non-terminal progress and cancellation requests are always retained.
    /// </summary>
    public TimeSpan? TerminalAudienceBroadcastRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Base notification payload rows older than this value are eligible only after no retained inbox or delivery
    /// record in the same tenant references them.
    /// </summary>
    public TimeSpan? OrphanNotificationRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Delay between marking an orphan payload for retention deletion and physically deleting it. This gives
    /// in-flight inbox/delivery work that started before the marker a later cleanup pass to materialize and cancel
    /// the marker before the payload is removed.
    /// </summary>
    public TimeSpan NotificationDeletionQuarantineDuration { get; set; } = TimeSpan.FromMinutes(5);

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

        if (string.IsNullOrWhiteSpace(CleanupWorkerLockName))
        {
            throw new InvalidOperationException($"{nameof(CleanupWorkerLockName)} must not be empty or whitespace.");
        }

        if (CleanupWorkerLockTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(CleanupWorkerLockTimeout)} must be greater than or equal to zero.");
        }

        if (CleanupWorkerPeriod.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(CleanupWorkerPeriod)} must not exceed {int.MaxValue} milliseconds.");
        }

        ValidateRetention(ReadUserNotificationRetention, nameof(ReadUserNotificationRetention));
        ValidateRetention(TerminalDeliveryRetention, nameof(TerminalDeliveryRetention));
        ValidateRetention(TerminalAudienceBroadcastRetention, nameof(TerminalAudienceBroadcastRetention));
        ValidateRetention(OrphanNotificationRetention, nameof(OrphanNotificationRetention));

        if (NotificationDeletionQuarantineDuration < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(NotificationDeletionQuarantineDuration)} must be greater than or equal to zero.");
        }
    }

    private static void ValidateRetention(TimeSpan? value, string propertyName)
    {
        if (value.HasValue && value.Value < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{propertyName} must be null or greater than or equal to zero.");
        }
    }
}
