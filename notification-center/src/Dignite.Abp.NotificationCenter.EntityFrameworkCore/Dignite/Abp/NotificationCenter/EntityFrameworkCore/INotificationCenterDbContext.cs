using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Abp.NotificationCenter.EntityFrameworkCore;

[ConnectionStringName(NotificationCenterDbProperties.ConnectionStringName)]
public interface INotificationCenterDbContext : IEfCoreDbContext
{
    DbSet<Notification> Notifications { get; }

    DbSet<UserNotification> UserNotifications { get; }

    DbSet<NotificationSubscription> NotificationSubscriptions { get; }

    DbSet<NotificationDeliveryRecord> NotificationDeliveries { get; }

    DbSet<NotificationDeliveryPreference> NotificationDeliveryPreferences { get; }

    DbSet<NotificationQuietHours> NotificationQuietHours { get; }

    DbSet<NotificationRetentionCleanupCursor> NotificationRetentionCleanupCursors { get; }
}
