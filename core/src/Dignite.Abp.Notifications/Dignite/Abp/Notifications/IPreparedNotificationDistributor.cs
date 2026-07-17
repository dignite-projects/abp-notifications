using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Optional capability used by the built-in publisher to distribute bounded explicit-recipient jobs after the
/// shared notification record has been prepared once.
/// </summary>
public interface IPreparedNotificationDistributor
{
    /// <summary>
    /// Gets whether this distributor can safely skip notification-record insertion for independently scheduled
    /// explicit-recipient batches.
    /// </summary>
    bool SupportsPreparedDistribution { get; }

    Task DistributePreparedAsync(
        NotificationInfo notification,
        Guid[] userIds,
        NotificationRecipientEligibilityMode recipientEligibilityMode,
        CancellationToken cancellationToken);
}
