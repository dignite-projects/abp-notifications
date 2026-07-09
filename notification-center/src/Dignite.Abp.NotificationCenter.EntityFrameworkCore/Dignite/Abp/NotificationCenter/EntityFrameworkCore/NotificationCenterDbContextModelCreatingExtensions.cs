using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dignite.Abp.NotificationCenter.EntityFrameworkCore;

public static class NotificationCenterDbContextModelCreatingExtensions
{
    public static void ConfigureNotificationCenter(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Notification>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "Notifications", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.NotificationName).IsRequired().HasMaxLength(NotificationCenterConsts.MaxNotificationNameLength);
            b.Property(x => x.EntityTypeName).HasMaxLength(NotificationCenterConsts.MaxEntityTypeNameLength);
            b.Property(x => x.EntityId).HasMaxLength(NotificationCenterConsts.MaxEntityIdLength);
            // Data is unbounded JSON (nvarchar(max) / TEXT) by default.

            b.HasIndex(x => new { x.TenantId, x.NotificationName, x.CreationTime });
        });

        builder.Entity<UserNotification>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "UserNotifications", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            // The real inbox query: a user's notifications filtered by state and ordered by time (fixes problem D).
            b.HasIndex(x => new { x.TenantId, x.UserId, x.State, x.CreationTime });
            // At most one inbox row per (user, notification).
            b.HasIndex(x => new { x.UserId, x.NotificationId }).IsUnique();
        });

        builder.Entity<NotificationSubscription>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "NotificationSubscriptions", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.NotificationName).IsRequired().HasMaxLength(NotificationCenterConsts.MaxNotificationNameLength);
            b.Property(x => x.EntityTypeName).HasMaxLength(NotificationCenterConsts.MaxEntityTypeNameLength);
            b.Property(x => x.EntityId).HasMaxLength(NotificationCenterConsts.MaxEntityIdLength);

            // Distribution lookup: subscribers of a notification, optionally scoped to an entity (fixes problem D).
            b.HasIndex(x => new { x.TenantId, x.NotificationName, x.EntityTypeName, x.EntityId });
            // At most one subscription per user/notification/entity scope.
            b.HasIndex(x => new { x.TenantId, x.UserId, x.NotificationName, x.EntityTypeName, x.EntityId }).IsUnique();
            // A user's own subscriptions.
            b.HasIndex(x => new { x.UserId, x.NotificationName });
        });
    }
}
