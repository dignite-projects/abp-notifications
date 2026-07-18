using Dignite.Abp.NotificationCenter.MongoDB;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs the shared retention cleanup scenarios against the MongoDB provider.</summary>
[Collection(MongoTestCollection.Name)]
public class NotificationRetentionCleanup_Tests :
    NotificationRetentionCleanup_Tests<AbpNotificationCenterMongoDbTestModule>
{
}
