using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;

namespace Dignite.Abp.NotificationCenter.EntityFrameworkCore;

[ConnectionStringName(NotificationCenterDbProperties.ConnectionStringName)]
public class NotificationCenterDbContext :
    AbpDbContext<NotificationCenterDbContext>,
    INotificationCenterDbContext,
    IHasEventInbox,
    IHasEventOutbox
{
    public DbSet<Notification> Notifications { get; set; } = default!;

    public DbSet<UserNotification> UserNotifications { get; set; } = default!;

    public DbSet<NotificationSubscription> NotificationSubscriptions { get; set; } = default!;

    public DbSet<NotificationDeliveryRecord> NotificationDeliveries { get; set; } = default!;

    public DbSet<NotificationDeliveryPreference> NotificationDeliveryPreferences { get; set; } = default!;

    public DbSet<NotificationQuietHours> NotificationQuietHours { get; set; } = default!;

    public DbSet<NotificationRetentionCleanupCursor> NotificationRetentionCleanupCursors { get; set; } = default!;

    // Transactional inbox/outbox support makes notification/inbox persistence and publishing
    // NotificationDeliveryRequestedEto atomic. The channel consumer persists its own delivery state before claiming it;
    // external side effects remain at least once unless the provider honors the idempotency key.
    public DbSet<IncomingEventRecord> IncomingEvents { get; set; } = default!;

    public DbSet<OutgoingEventRecord> OutgoingEvents { get; set; } = default!;

    public NotificationCenterDbContext(DbContextOptions<NotificationCenterDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigureEventInbox();
        builder.ConfigureEventOutbox();
        builder.ConfigureNotificationCenter();
    }
}
