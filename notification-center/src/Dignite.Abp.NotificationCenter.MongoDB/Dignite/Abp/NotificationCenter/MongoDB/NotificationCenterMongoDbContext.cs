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
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "Notifications");
        modelBuilder.Entity<UserNotification>(b =>
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "UserNotifications");
        modelBuilder.Entity<NotificationSubscription>(b =>
            b.CollectionName = NotificationCenterDbProperties.DbTablePrefix + "NotificationSubscriptions");
    }
}
