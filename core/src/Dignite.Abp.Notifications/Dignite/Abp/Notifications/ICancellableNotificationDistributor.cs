using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Optional cancellation capability for an <see cref="INotificationDistributor"/>.
/// </summary>
/// <remarks>
/// Kept separate from the original contract so existing custom distributors remain source and binary compatible.
/// </remarks>
public interface ICancellableNotificationDistributor
{
    /// <summary>
    /// Distributes while observing cancellation between bounded candidate, persistence, and delivery batches.
    /// </summary>
    Task DistributeAsync(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds,
        CancellationToken cancellationToken);

    /// <summary>Cancellation-aware trusted-system explicit-recipient distribution.</summary>
    Task DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
        NotificationInfo notification,
        Guid[] userIds,
        Guid[]? excludedUserIds,
        CancellationToken cancellationToken);
}
