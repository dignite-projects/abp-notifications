using Dignite.Abp.NotificationCenter.MongoDB;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs the shared <see cref="IUserNotificationAppService"/> scenarios against the MongoDB provider.</summary>
[Collection(MongoTestCollection.Name)]
public class UserNotificationAppService_Tests : UserNotificationAppService_Tests<AbpNotificationCenterMongoDbTestModule>
{
}
