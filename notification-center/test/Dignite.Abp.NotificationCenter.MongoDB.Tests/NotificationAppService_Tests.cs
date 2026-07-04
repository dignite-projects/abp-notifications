using Dignite.Abp.NotificationCenter.MongoDB;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs the shared <see cref="INotificationAppService"/> scenarios against the MongoDB provider.</summary>
[Collection(MongoTestCollection.Name)]
public class NotificationAppService_Tests : NotificationAppService_Tests<AbpNotificationCenterMongoDbTestModule>
{
}
