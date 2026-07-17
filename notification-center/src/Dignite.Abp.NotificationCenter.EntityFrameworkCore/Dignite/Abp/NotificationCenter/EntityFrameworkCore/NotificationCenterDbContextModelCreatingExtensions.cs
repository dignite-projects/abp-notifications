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
            b.Property(x => x.NotificationNameKey).IsRequired().IsUnicode(false).IsFixedLength()
                .HasMaxLength(NotificationCenterConsts.SubscriptionIdentityKeyLength);
            b.Property(x => x.EntityTypeName).HasMaxLength(NotificationCenterConsts.MaxEntityTypeNameLength);
            b.Property(x => x.EntityId).HasMaxLength(NotificationCenterConsts.MaxEntityIdLength);
            b.Property(x => x.ScopeKey).IsRequired().IsUnicode(false).IsFixedLength()
                .HasMaxLength(NotificationCenterConsts.SubscriptionIdentityKeyLength);

            // Exact/fallback distribution lookup. Non-null keys avoid provider-specific nullable-index semantics.
            b.HasIndex(x => new { x.TenantKey, x.NotificationNameKey, x.ScopeKey });
            // At most one subscription per tenant/user/notification/entity scope, including host + definition-wide rows.
            b.HasIndex(x => new { x.TenantKey, x.UserId, x.NotificationNameKey, x.ScopeKey }).IsUnique();
            // A user's own subscriptions.
            b.HasIndex(x => new { x.TenantKey, x.UserId, x.NotificationNameKey });
        });

        builder.Entity<NotificationDeliveryRecord>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "NotificationDeliveries", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Channel).IsRequired().HasMaxLength(NotificationCenterConsts.MaxDeliveryChannelLength);
            b.Property(x => x.ChannelKey).IsRequired().HasMaxLength(NotificationCenterConsts.MaxDeliveryChannelLength);
            b.Property(x => x.IdempotencyKey).IsRequired().IsUnicode(false)
                .HasMaxLength(NotificationCenterConsts.DeliveryIdempotencyKeyLength);
            b.Property(x => x.LastFailureCode).HasMaxLength(NotificationCenterConsts.MaxDeliveryFailureCodeLength);
            b.Property(x => x.LastFailureMessage).HasMaxLength(NotificationCenterConsts.MaxDeliveryFailureMessageLength);

            b.HasIndex(x => new { x.TenantKey, x.NotificationId, x.UserId, x.ChannelKey }).IsUnique();
            b.HasIndex(x => new { x.State, x.NextAttemptTime });
            b.HasIndex(x => new { x.State, x.LeaseExpirationTime });
        });
    }
}
