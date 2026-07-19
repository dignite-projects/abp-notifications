using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;
using Volo.Abp.MongoDB.DistributedEvents;
using NotificationQuietHoursEntity = Dignite.Abp.NotificationCenter.NotificationQuietHours;

namespace Dignite.Abp.NotificationCenter.MongoDB;

[ConnectionStringName(NotificationCenterDbProperties.ConnectionStringName)]
public class NotificationCenterMongoDbContext : AbpMongoDbContext, INotificationCenterMongoDbContext
{
    public IMongoCollection<Notification> Notifications => Collection<Notification>();

    public IMongoCollection<UserNotification> UserNotifications => Collection<UserNotification>();

    public IMongoCollection<NotificationSubscription> NotificationSubscriptions => Collection<NotificationSubscription>();

    public IMongoCollection<NotificationDeliveryRecord> NotificationDeliveries => Collection<NotificationDeliveryRecord>();

    public IMongoCollection<NotificationDeliveryPreference> NotificationDeliveryPreferences => Collection<NotificationDeliveryPreference>();

    public IMongoCollection<NotificationQuietHours> NotificationQuietHours => Collection<NotificationQuietHours>();

    public IMongoCollection<NotificationRetentionCleanupCursor> NotificationRetentionCleanupCursors => Collection<NotificationRetentionCleanupCursor>();

    public IMongoCollection<NotificationAudienceBroadcastState> NotificationAudienceBroadcastStates => Collection<NotificationAudienceBroadcastState>();

    public IMongoCollection<IncomingEventRecord> IncomingEvents => Collection<IncomingEventRecord>();

    public IMongoCollection<OutgoingEventRecord> OutgoingEvents => Collection<OutgoingEventRecord>();

    protected override void CreateModel(IMongoModelBuilder modelBuilder)
    {
        base.CreateModel(modelBuilder);

        modelBuilder.ConfigureEventInbox();
        modelBuilder.ConfigureEventOutbox();

        // ABP's MongoDB model builder supplies the conventional collection names but does not add the
        // query indexes that its EF Core model supplies. Keep both providers aligned with the sender,
        // processor, duplicate-detection, and cleanup query shapes.
        modelBuilder.Entity<IncomingEventRecord>(b =>
        {
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(IncomingEventRecord.Status))
                        .Ascending(nameof(IncomingEventRecord.CreationTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(IncomingEventRecord.MessageId)),
                    new CreateIndexOptions { Unique = true }));
            });
        });

        modelBuilder.Entity<OutgoingEventRecord>(b =>
        {
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(OutgoingEventRecord.CreationTime))));
            });
        });

        modelBuilder.Entity<Notification>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "Notifications";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(Notification.TenantId))
                        .Ascending(nameof(Notification.NotificationName))
                        .Descending(nameof(Notification.CreationTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(Notification.TenantId))
                        .Ascending(nameof(Notification.CreationTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(Notification.CreationTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(Notification.TenantId))
                        .Ascending(nameof(Notification.RetentionDeletionTime))
                        .Ascending(nameof(Notification.CreationTime))));
            });
        });

        modelBuilder.Entity<UserNotification>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "UserNotifications";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(UserNotification.TenantId))
                        .Ascending(nameof(UserNotification.UserId))
                        .Ascending(nameof(UserNotification.State))
                        .Descending(nameof(UserNotification.CreationTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(UserNotification.TenantId))
                        .Ascending(nameof(UserNotification.State))
                        .Ascending(nameof(UserNotification.CreationTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(UserNotification.State))
                        .Ascending(nameof(UserNotification.CreationTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(UserNotification.TenantId))
                        .Ascending(nameof(UserNotification.NotificationId))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(UserNotification.UserId))
                        .Ascending(nameof(UserNotification.NotificationId)),
                    new CreateIndexOptions { Unique = true }));
            });
        });

        modelBuilder.Entity<NotificationSubscription>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "NotificationSubscriptions";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationSubscription.TenantKey))
                        .Ascending(nameof(NotificationSubscription.NotificationNameKey))
                        .Ascending(nameof(NotificationSubscription.ScopeKey))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationSubscription.TenantKey))
                        .Ascending(nameof(NotificationSubscription.UserId))
                        .Ascending(nameof(NotificationSubscription.NotificationNameKey))
                        .Ascending(nameof(NotificationSubscription.ScopeKey)),
                    new CreateIndexOptions { Unique = true }));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationSubscription.TenantKey))
                        .Ascending(nameof(NotificationSubscription.UserId))
                        .Ascending(nameof(NotificationSubscription.NotificationNameKey))));
            });
        });

        modelBuilder.Entity<NotificationDeliveryRecord>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "NotificationDeliveries";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryRecord.TenantKey))
                        .Ascending(nameof(NotificationDeliveryRecord.NotificationId))
                        .Ascending(nameof(NotificationDeliveryRecord.UserId))
                        .Ascending(nameof(NotificationDeliveryRecord.ChannelKey)),
                    new CreateIndexOptions { Unique = true }));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryRecord.TenantKey))
                        .Ascending(nameof(NotificationDeliveryRecord.NotificationId))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryRecord.TenantKey))
                        .Ascending(nameof(NotificationDeliveryRecord.State))
                        .Ascending(nameof(NotificationDeliveryRecord.CompletedTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryRecord.State))
                        .Ascending(nameof(NotificationDeliveryRecord.CompletedTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryRecord.State))
                        .Ascending(nameof(NotificationDeliveryRecord.NextAttemptTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryRecord.State))
                        .Ascending(nameof(NotificationDeliveryRecord.LeaseExpirationTime))));
            });
        });

        modelBuilder.Entity<NotificationDeliveryPreference>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "NotificationDeliveryPreferences";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryPreference.TenantKey))
                        .Ascending(nameof(NotificationDeliveryPreference.UserId))
                        .Ascending(nameof(NotificationDeliveryPreference.NotificationNameKey))
                        .Ascending(nameof(NotificationDeliveryPreference.ChannelKey)),
                    new CreateIndexOptions { Unique = true }));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationDeliveryPreference.TenantKey))
                        .Ascending(nameof(NotificationDeliveryPreference.UserId))));
            });
        });

        modelBuilder.Entity<NotificationQuietHours>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "NotificationQuietHours";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationQuietHoursEntity.TenantKey))
                        .Ascending(nameof(NotificationQuietHoursEntity.UserId)),
                    new CreateIndexOptions { Unique = true }));
            });
        });

        modelBuilder.Entity<NotificationRetentionCleanupCursor>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "NotificationRetentionCleanupCursors";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationRetentionCleanupCursor.IsTenantScoped))
                        .Ascending(nameof(NotificationRetentionCleanupCursor.TenantKey))
                        .Ascending(nameof(NotificationRetentionCleanupCursor.RecordKind)),
                    new CreateIndexOptions { Unique = true }));
            });
        });

        modelBuilder.Entity<NotificationAudienceBroadcastState>(b =>
        {
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "NotificationAudienceBroadcastStates";
            b.ConfigureIndexes(indexes =>
            {
                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationAudienceBroadcastState.TenantKey))
                        .Ascending(nameof(NotificationAudienceBroadcastState.Status))
                        .Ascending(nameof(NotificationAudienceBroadcastState.CompletionTime))));

                indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(nameof(NotificationAudienceBroadcastState.Status))
                        .Ascending(nameof(NotificationAudienceBroadcastState.CompletionTime))));
            });
        });
    }
}
