using Dignite.Abp.NotificationCenter.MongoDB;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

[Collection(MongoTestCollection.Name)]
public class NotificationAudienceBroadcastProgressStore_Tests :
    NotificationAudienceBroadcastProgressStore_Tests<AbpNotificationCenterMongoDbTestModule>
{
}
