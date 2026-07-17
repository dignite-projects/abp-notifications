using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Dignite.Abp.NotificationCenter.MongoDB;

[ConnectionStringName(NotificationCenterDbProperties.ConnectionStringName)]
public class NotificationCenterMongoDbContext : AbpMongoDbContext, INotificationCenterMongoDbContext
{
    public IMongoCollection<Notification> Notifications => Collection<Notification>();

    public IMongoCollection<UserNotification> UserNotifications => Collection<UserNotification>();

    public IMongoCollection<NotificationSubscription> NotificationSubscriptions => Collection<NotificationSubscription>();

    protected override void CreateModel(IMongoModelBuilder modelBuilder)
    {
        base.CreateModel(modelBuilder);

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
    }
}
