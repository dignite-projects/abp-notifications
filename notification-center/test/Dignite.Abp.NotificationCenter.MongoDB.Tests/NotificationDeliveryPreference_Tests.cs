using Dignite.Abp.NotificationCenter.MongoDB;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

[Collection(MongoTestCollection.Name)]
public class NotificationDeliveryPreference_Tests :
    NotificationDeliveryPreference_Tests<AbpNotificationCenterMongoDbTestModule>
{
}
