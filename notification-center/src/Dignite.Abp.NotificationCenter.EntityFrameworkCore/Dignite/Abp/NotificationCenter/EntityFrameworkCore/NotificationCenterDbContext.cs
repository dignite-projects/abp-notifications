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

    // Transactional inbox/outbox support. When an app enables it, persisting notification/delivery state and
    // publishing NotificationDeliveryWorkEto become atomic. ABP's inbox and the delivery lease make internal work
    // idempotent; external side effects remain at least once unless the provider honors the idempotency key.
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
