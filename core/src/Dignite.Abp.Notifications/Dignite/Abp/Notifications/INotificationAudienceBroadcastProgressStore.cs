using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Stores observable progress and cancellation requests for audience broadcasts.
/// </summary>
public interface INotificationAudienceBroadcastProgressStore
{
    Task<NotificationAudienceBroadcastProgress?> GetAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task RecordStartedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default);

    Task RecordPageCompletedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        long pageIndex,
        long candidateCount,
        string? nextCursor,
        bool hasMore,
        DateTime updateTime,
        CancellationToken cancellationToken = default);

    Task RecordCompletedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default);

    Task<bool> RequestCancellationAsync(
        Guid notificationId,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default);

    Task<bool> IsCancellationRequestedAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task RecordCanceledAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default);

    Task RecordFailedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        string errorMessage,
        DateTime updateTime,
        CancellationToken cancellationToken = default);
}
