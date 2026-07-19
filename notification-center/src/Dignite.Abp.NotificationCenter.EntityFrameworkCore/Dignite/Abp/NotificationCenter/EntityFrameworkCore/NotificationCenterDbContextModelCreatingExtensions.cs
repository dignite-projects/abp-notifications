using Dignite.Abp.Notifications;
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
            // Retention cleanup scans old base payload rows and deletes only tenant-local orphans.
            b.HasIndex(x => new { x.TenantId, x.CreationTime });
            b.HasIndex(x => x.CreationTime);
            // Two-phase payload cleanup physically deletes only after a marker quarantine period.
            b.HasIndex(x => new { x.TenantId, x.RetentionDeletionTime, x.CreationTime });
        });

        builder.Entity<UserNotification>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "UserNotifications", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            // The real inbox query: a user's notifications filtered by state and ordered by time (fixes problem D).
            b.HasIndex(x => new { x.TenantId, x.UserId, x.State, x.CreationTime });
            // Retention cleanup deletes old read inbox rows without loading unrelated users.
            b.HasIndex(x => new { x.TenantId, x.State, x.CreationTime });
            b.HasIndex(x => new { x.State, x.CreationTime });
            // Base payload cleanup checks tenant-local inbox references before deleting a notification row.
            b.HasIndex(x => new { x.TenantId, x.NotificationId });
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
            b.Property(x => x.NotificationName).IsRequired()
                .HasMaxLength(NotificationCenterConsts.MaxNotificationNameLength);
            b.Property(x => x.EntityTypeName).HasMaxLength(NotificationCenterConsts.MaxEntityTypeNameLength);
            b.Property(x => x.EntityId).HasMaxLength(NotificationCenterConsts.MaxEntityIdLength);
            // Data is a stable System.Text.Json snapshot and remains unbounded.
            b.Property(x => x.PreferenceReasonCode)
                .HasMaxLength(NotificationDeliveryPreferenceReasonCodes.MaxLength);
            b.Property(x => x.LastFailureCode).HasMaxLength(NotificationCenterConsts.MaxDeliveryFailureCodeLength);
            b.Property(x => x.LastFailureMessage).HasMaxLength(NotificationCenterConsts.MaxDeliveryFailureMessageLength);
            b.Property(x => x.LastForceDeliveryReasonCode)
                .HasMaxLength(NotificationDeliveryOverrideReasonCodes.MaxLength);

            b.HasIndex(x => new { x.TenantKey, x.NotificationId, x.UserId, x.ChannelKey }).IsUnique();
            // Base payload cleanup checks tenant-local delivery references before deleting a notification row.
            b.HasIndex(x => new { x.TenantKey, x.NotificationId });
            // Retention cleanup deletes only terminal records whose completion timestamp has aged out.
            b.HasIndex(x => new { x.TenantKey, x.State, x.CompletedTime });
            b.HasIndex(x => new { x.State, x.CompletedTime });
            b.HasIndex(x => new { x.State, x.NextAttemptTime });
            b.HasIndex(x => new { x.State, x.LeaseExpirationTime });
        });

        builder.Entity<NotificationDeliveryPreference>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "NotificationDeliveryPreferences", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.NotificationName).HasMaxLength(NotificationCenterConsts.MaxNotificationNameLength);
            b.Property(x => x.NotificationNameKey).IsRequired().IsUnicode(false).IsFixedLength()
                .HasMaxLength(NotificationCenterConsts.DeliveryPreferenceIdentityKeyLength);
            b.Property(x => x.Channel).HasMaxLength(NotificationCenterConsts.MaxDeliveryChannelLength);
            b.Property(x => x.ChannelKey).IsRequired().IsUnicode(false).IsFixedLength()
                .HasMaxLength(NotificationCenterConsts.DeliveryPreferenceIdentityKeyLength);

            b.HasIndex(x => new { x.TenantKey, x.UserId, x.NotificationNameKey, x.ChannelKey }).IsUnique();
            b.HasIndex(x => new { x.TenantKey, x.UserId });
        });

        builder.Entity<NotificationQuietHours>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "NotificationQuietHours", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.TimeZoneId).IsRequired().HasMaxLength(NotificationCenterConsts.MaxTimeZoneIdLength);
            b.HasIndex(x => new { x.TenantKey, x.UserId }).IsUnique();
        });

        builder.Entity<NotificationRetentionCleanupCursor>(b =>
        {
            b.ToTable(NotificationCenterDbProperties.DbTablePrefix + "NotificationRetentionCleanupCursors", NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            // One cursor per cleanup scope and record kind lets bounded passes continue past retained prefixes.
            b.HasIndex(x => new { x.IsTenantScoped, x.TenantKey, x.RecordKind }).IsUnique();
        });

        builder.Entity<NotificationAudienceBroadcastState>(b =>
        {
            b.ToTable(
                NotificationCenterDbProperties.DbTablePrefix + "NotificationAudienceBroadcastStates",
                NotificationCenterDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.NotificationName).IsRequired()
                .HasMaxLength(NotificationCenterConsts.MaxNotificationNameLength);
            b.Property(x => x.AudienceName).IsRequired()
                .HasMaxLength(NotificationCenterConsts.MaxAudienceNameLength);
            b.Property(x => x.ContinuationToken)
                .HasMaxLength(NotificationCenterConsts.MaxBroadcastContinuationTokenLength);
            b.Property(x => x.FailureCode)
                .HasMaxLength(NotificationCenterConsts.MaxBroadcastFailureCodeLength);
            b.Property(x => x.FailureMessage)
                .HasMaxLength(NotificationCenterConsts.MaxBroadcastFailureMessageLength);

            b.HasIndex(x => new { x.TenantKey, x.Status, x.CompletionTime });
            b.HasIndex(x => new { x.Status, x.CompletionTime });
        });
    }
}
