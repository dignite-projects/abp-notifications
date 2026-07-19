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

    public long ScannedAudienceBroadcastStates { get; set; }

    public long DeletedAudienceBroadcastStates { get; set; }

    public long SkippedAudienceBroadcastStates { get; set; }

    public long AudienceBroadcastStateErrors { get; set; }

    public long ScannedCount => ScannedNotifications + ScannedUserNotifications + ScannedDeliveries +
                                ScannedAudienceBroadcastStates;

    public long DeletedCount => DeletedNotifications + DeletedUserNotifications + DeletedDeliveries +
                                DeletedAudienceBroadcastStates;

    public long SkippedCount => SkippedNotifications + SkippedUserNotifications + SkippedDeliveries +
                                SkippedAudienceBroadcastStates;

    public long ErrorCount => NotificationErrors + UserNotificationErrors + DeliveryErrors +
                              AudienceBroadcastStateErrors;
}
