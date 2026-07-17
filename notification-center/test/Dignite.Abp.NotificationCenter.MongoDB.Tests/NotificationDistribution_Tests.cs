using Dignite.Abp.NotificationCenter.MongoDB;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs shared recipient-semantics scenarios against MongoDB.</summary>
[Collection(MongoTestCollection.Name)]
public class NotificationDistribution_Tests
    : NotificationDistribution_Tests<AbpNotificationCenterMongoDbTestModule>
{
}
