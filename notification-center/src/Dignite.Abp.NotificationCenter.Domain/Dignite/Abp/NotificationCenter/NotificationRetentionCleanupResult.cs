using System;

namespace Dignite.Abp.NotificationCenter;

public class NotificationRetentionCleanupResult
{
    public bool IsDryRun { get; set; }

    public DateTime StartedTime { get; set; }

    public DateTime? CompletedTime { get; set; }

    public long ScannedNotifications { get; set; }

    public long DeletedNotifications { get; set; }

    public long SkippedNotifications { get; set; }

    public long NotificationErrors { get; set; }

    public DateTime? OldestRetainedNotificationCreationTime { get; set; }

    public long ScannedUserNotifications { get; set; }

    public long DeletedUserNotifications { get; set; }

    public long SkippedUserNotifications { get; set; }

    public long UserNotificationErrors { get; set; }

    public DateTime? OldestRetainedUserNotificationCreationTime { get; set; }

    public long ScannedDeliveries { get; set; }

    public long DeletedDeliveries { get; set; }

    public long SkippedDeliveries { get; set; }

    public long DeliveryErrors { get; set; }

    public DateTime? OldestRetainedDeliveryCreationTime { get; set; }

    public long ScannedCount => ScannedNotifications + ScannedUserNotifications + ScannedDeliveries;

    public long DeletedCount => DeletedNotifications + DeletedUserNotifications + DeletedDeliveries;

    public long SkippedCount => SkippedNotifications + SkippedUserNotifications + SkippedDeliveries;

    public long ErrorCount => NotificationErrors + UserNotificationErrors + DeliveryErrors;
}
