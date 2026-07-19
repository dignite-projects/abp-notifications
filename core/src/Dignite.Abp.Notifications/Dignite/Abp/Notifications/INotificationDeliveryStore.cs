using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// State store for one recipient/channel work item. Implementations must make claims visible before returning and
/// compare the lease token on every terminal transition. NotificationCenter replaces the default process-local
/// implementation with a durable provider-neutral store.
/// </summary>
public interface INotificationDeliveryStore
{
    /// <summary>
    /// Idempotently materializes the consumer-owned work snapshot and claims it in one independently committed
    /// store operation. A newly created record must be inserted directly in the claimed state so an ambient event
    /// inbox transaction never has to expose an uncommitted pending row to a nested claim operation.
    /// </summary>
    Task<NotificationDeliveryClaim?> EnsureCreatedAndTryClaimAsync(
        NotificationDeliveryWorkEto workItem,
        DateTime now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default);

    Task EnsureCreatedAsync(
        NotificationDeliveryWorkEto workItem,
        CancellationToken cancellationToken = default);

    Task<NotificationDeliveryClaim?> TryClaimAsync(
        Guid deliveryId,
        Guid? tenantId,
        DateTime now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default);

    Task<bool> MarkSucceededAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime completedAt,
        CancellationToken cancellationToken = default);

    Task<bool> MarkSuppressedAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime completedAt,
        string reasonCode,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime failedAt,
        string failureCode,
        DateTime? nextAttemptTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets due work across all tenants for infrastructure retry processing. Implementations must preserve each
    /// returned item's TenantId and must not expose this cross-tenant operation through an end-user API.
    /// </summary>
    Task<IReadOnlyList<NotificationDeliveryWorkEto>> GetDueWorkItemsAsync(
        DateTime now,
        int maxResultCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues a failed or dead-lettered delivery in the explicitly supplied tenant without changing the
    /// producer-resolved delivery intent or preference diagnostics.
    /// </summary>
    Task<bool> RetryAsync(
        Guid deliveryId,
        Guid? tenantId,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly overrides a suppressed delivery in the supplied tenant and records a non-sensitive audit entry.
    /// </summary>
    Task<bool> ForceDeliverAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid actorId,
        DateTime now,
        string reasonCode,
        CancellationToken cancellationToken = default);
}
