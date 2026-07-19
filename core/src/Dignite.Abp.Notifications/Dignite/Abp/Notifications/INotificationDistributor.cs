using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Resolves, persists, and publishes delivery work for a notification.
/// </summary>
public interface INotificationDistributor
{
    /// <summary>
    /// Distributes a notification to subscribers or to an explicit set of users.
    /// </summary>
    /// <param name="notification">
    /// The notification to distribute. Its <see cref="NotificationInfo.TenantId"/> is authoritative for the complete
    /// operation; <see langword="null"/> means host and never falls back to the ambient tenant.
    /// </param>
    /// <param name="userIds">
    /// The recipients. <see langword="null"/> resolves recipients from subscriptions; an empty array is an
    /// intentional no-op; a non-empty array targets those users explicitly. Duplicate IDs are removed before
    /// persistence and delivery publication. Definition feature and permission requirements are enforced for
    /// both explicit and subscription-derived candidates.
    /// </param>
    /// <param name="excludedUserIds">Optional user IDs to remove from the resolved recipient set.</param>
    /// <param name="cancellationToken">Cancellation observed between bounded pipeline operations.</param>
    Task DistributeAsync(
        NotificationInfo notification,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Distributes to explicit trusted-system recipients without definition eligibility checks. The bypass is
    /// warning-logged by the distributor and cannot resolve recipients from subscriptions.
    /// </summary>
    Task DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
        NotificationInfo notification,
        Guid[] userIds,
        Guid[]? excludedUserIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Distributes an already-persisted notification to one bounded explicit-recipient batch. Publisher and audience
    /// jobs use this supported extension point so the shared notification record is prepared exactly once.
    /// </summary>
    Task DistributePreparedAsync(
        NotificationInfo notification,
        Guid[] userIds,
        NotificationRecipientEligibilityMode recipientEligibilityMode,
        CancellationToken cancellationToken = default);
}
