using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Enqueues explicit-tenant large-audience broadcasts through bounded recipient-source pages.
/// </summary>
public interface INotificationAudienceBroadcaster
{
    /// <summary>
    /// Starts one tenant/host-scoped broadcast. The supplied tenant id is authoritative for every queued page.
    /// </summary>
    Task<NotificationAudienceBroadcastTenantResult> EnqueueTenantBroadcastAsync(
        NotificationAudienceTenantBroadcastRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a host-orchestrated broadcast by enqueueing one isolated tenant job per supplied tenant id.
    /// </summary>
    Task<NotificationAudienceBroadcastResult> EnqueueHostBroadcastAsync(
        NotificationAudienceHostBroadcastRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets observable progress for a previously enqueued tenant/host broadcast.
    /// </summary>
    Task<NotificationAudienceBroadcastProgress?> GetTenantBroadcastProgressAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation for a previously enqueued tenant/host broadcast.
    /// </summary>
    Task<bool> CancelTenantBroadcastAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}
