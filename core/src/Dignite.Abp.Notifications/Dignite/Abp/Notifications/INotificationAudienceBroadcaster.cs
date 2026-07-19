using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Enqueues tenant-or-host large-audience broadcasts through bounded recipient-source pages.
/// </summary>
public interface INotificationAudienceBroadcaster
{
    /// <summary>
    /// Starts one tenant-or-host scoped broadcast. The supplied tenant id is authoritative for every queued page.
    /// </summary>
    Task<NotificationAudienceBroadcastEnqueueResult> EnqueueAsync(
        NotificationAudienceBroadcastRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues one isolated tenant broadcast per supplied tenant id. This host-authorized operation does not
    /// include host users.
    /// </summary>
    Task<NotificationAudienceMultiTenantBroadcastResult> EnqueueForTenantsAsync(
        NotificationAudienceMultiTenantBroadcastRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets observable progress for a previously enqueued tenant-or-host scoped broadcast.
    /// </summary>
    Task<NotificationAudienceBroadcastProgress?> GetProgressAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation for a previously enqueued tenant-or-host scoped broadcast.
    /// </summary>
    Task<bool> CancelAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}
