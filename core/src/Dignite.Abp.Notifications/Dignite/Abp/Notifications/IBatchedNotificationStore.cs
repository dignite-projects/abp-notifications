using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Optional bounded-distribution capability for an <see cref="INotificationStore"/>.
/// </summary>
/// <remarks>
/// The interface is separate so existing custom stores remain source and binary compatible. The built-in
/// NotificationCenter stores implement it. Custom stores should implement it before enabling large fan-outs;
/// otherwise the distributor uses the legacy materializing/per-row compatibility path.
/// </remarks>
public interface IBatchedNotificationStore
{
    /// <summary>
    /// Gets the next stable, ordered page of distinct subscription recipient IDs in the current tenant.
    /// <paramref name="afterUserId"/> is an exclusive keyset cursor; <see langword="null"/> starts the scan.
    /// </summary>
    Task<List<Guid>> GetSubscriptionUserIdsAsync(
        string notificationName,
        string? entityTypeName,
        string? entityId,
        Guid? afterUserId,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts the shared notification record with cancellation support.</summary>
    Task InsertNotificationAsync(
        NotificationInfo notification,
        CancellationToken cancellationToken);

    /// <summary>Inserts one already-bounded group of inbox rows.</summary>
    Task InsertUserNotificationsAsync(
        IReadOnlyCollection<UserNotificationInfo> userNotifications,
        CancellationToken cancellationToken = default);
}
