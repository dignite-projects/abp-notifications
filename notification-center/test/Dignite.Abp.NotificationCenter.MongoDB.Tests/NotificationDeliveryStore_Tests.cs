using Dignite.Abp.NotificationCenter.MongoDB;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

[Collection(MongoTestCollection.Name)]
public class NotificationDeliveryStore_Tests :
    NotificationDeliveryStore_Tests<AbpNotificationCenterMongoDbTestModule>
{
}
