using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.NotificationCenter.MongoDB;

/// <summary>
/// Reports the transaction capability of the MongoDB deployment used by
/// <see cref="NotificationCenterMongoDbContext"/>.
/// </summary>
public interface INotificationCenterMongoDbOutboxCapabilityChecker
{
    /// <summary>Checks the MongoDB connection resolved for the current tenant/host context.</summary>
    Task<NotificationCenterMongoDbOutboxCapability> CheckAsync(
        CancellationToken cancellationToken = default);
}
