using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

[ExposeServices(typeof(INotificationAudienceBroadcastProgressStore))]
public class InMemoryNotificationAudienceBroadcastProgressStore :
    INotificationAudienceBroadcastProgressStore,
    ISingletonDependency
{
    private readonly ConcurrentDictionary<string, NotificationAudienceBroadcastProgress> _progresses = new();

    public virtual Task<NotificationAudienceBroadcastProgress?> GetAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _progresses.TryGetValue(CreateKey(notificationId, tenantId), out var progress)
                ? progress.Clone()
                : null);
    }

    public virtual Task RecordStartedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _progresses.AddOrUpdate(
            CreateKey(notification.Id, tenantId),
            _ => NewProgress(notification, audienceName, tenantId, NotificationAudienceBroadcastStatus.Enqueued, updateTime),
            (_, existing) =>
            {
                if (IsTerminal(existing.Status))
                {
                    return existing;
                }

                existing.NotificationName = notification.NotificationName;
                existing.AudienceName = audienceName;
                existing.Status = NotificationAudienceBroadcastStatus.Enqueued;
                existing.LastUpdatedTime = updateTime;
                existing.ErrorMessage = null;
                return existing;
            });
        return Task.CompletedTask;
    }

    public virtual Task RecordPageCompletedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        long pageIndex,
        long candidateCount,
        string? nextCursor,
        bool hasMore,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _progresses.AddOrUpdate(
            CreateKey(notification.Id, tenantId),
            _ =>
            {
                var progress = NewProgress(
                    notification,
                    audienceName,
                    tenantId,
                    NotificationAudienceBroadcastStatus.Running,
                    updateTime);
                progress.CompletedPageCount = pageIndex + 1;
                progress.CandidateCount = candidateCount;
                progress.NextCursor = nextCursor;
                progress.HasMore = hasMore;
                return progress;
            },
            (_, existing) =>
            {
                if (IsTerminal(existing.Status) || pageIndex < existing.CompletedPageCount)
                {
                    return existing;
                }

                if (pageIndex >= existing.CompletedPageCount)
                {
                    existing.CandidateCount += candidateCount;
                    existing.CompletedPageCount = pageIndex + 1;
                }

                existing.Status = existing.IsCancellationRequested
                    ? NotificationAudienceBroadcastStatus.CancellationRequested
                    : NotificationAudienceBroadcastStatus.Running;
                existing.NextCursor = nextCursor;
                existing.HasMore = hasMore;
                existing.LastUpdatedTime = updateTime;
                existing.ErrorMessage = null;
                return existing;
            });
        return Task.CompletedTask;
    }

    public virtual Task RecordCompletedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _progresses.AddOrUpdate(
            CreateKey(notification.Id, tenantId),
            _ => NewProgress(notification, audienceName, tenantId, NotificationAudienceBroadcastStatus.Completed, updateTime),
            (_, existing) =>
            {
                if (IsTerminal(existing.Status))
                {
                    return existing;
                }

                existing.Status = NotificationAudienceBroadcastStatus.Completed;
                existing.HasMore = false;
                existing.NextCursor = null;
                existing.LastUpdatedTime = updateTime;
                existing.ErrorMessage = null;
                return existing;
            });
        return Task.CompletedTask;
    }

    public virtual Task<bool> RequestCancellationAsync(
        Guid notificationId,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = CreateKey(notificationId, tenantId);
        if (!_progresses.TryGetValue(key, out var existing) ||
            IsTerminal(existing.Status))
        {
            return Task.FromResult(false);
        }

        existing.IsCancellationRequested = true;
        existing.Status = NotificationAudienceBroadcastStatus.CancellationRequested;
        existing.LastUpdatedTime = updateTime;
        return Task.FromResult(true);
    }

    public virtual Task<bool> IsCancellationRequestedAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            _progresses.TryGetValue(CreateKey(notificationId, tenantId), out var existing) &&
            existing.IsCancellationRequested);
    }

    public virtual Task RecordCanceledAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _progresses.AddOrUpdate(
            CreateKey(notification.Id, tenantId),
            _ => NewProgress(notification, audienceName, tenantId, NotificationAudienceBroadcastStatus.Canceled, updateTime),
            (_, existing) =>
            {
                if (IsTerminal(existing.Status))
                {
                    return existing;
                }

                existing.Status = NotificationAudienceBroadcastStatus.Canceled;
                existing.IsCancellationRequested = true;
                existing.HasMore = false;
                existing.LastUpdatedTime = updateTime;
                return existing;
            });
        return Task.CompletedTask;
    }

    public virtual Task RecordFailedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        string errorMessage,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _progresses.AddOrUpdate(
            CreateKey(notification.Id, tenantId),
            _ =>
            {
                var progress = NewProgress(
                    notification,
                    audienceName,
                    tenantId,
                    NotificationAudienceBroadcastStatus.Failed,
                    updateTime);
                progress.ErrorMessage = errorMessage;
                return progress;
            },
            (_, existing) =>
            {
                if (IsTerminal(existing.Status))
                {
                    return existing;
                }

                existing.Status = NotificationAudienceBroadcastStatus.Failed;
                existing.ErrorMessage = errorMessage;
                existing.LastUpdatedTime = updateTime;
                return existing;
            });
        return Task.CompletedTask;
    }

    protected virtual NotificationAudienceBroadcastProgress NewProgress(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        NotificationAudienceBroadcastStatus status,
        DateTime updateTime)
    {
        return new NotificationAudienceBroadcastProgress
        {
            TenantId = tenantId,
            NotificationId = notification.Id,
            NotificationName = notification.NotificationName,
            AudienceName = audienceName,
            Status = status,
            HasMore = status is NotificationAudienceBroadcastStatus.Enqueued or NotificationAudienceBroadcastStatus.Running,
            LastUpdatedTime = updateTime
        };
    }

    private static bool IsTerminal(NotificationAudienceBroadcastStatus status)
    {
        return status is NotificationAudienceBroadcastStatus.Completed
            or NotificationAudienceBroadcastStatus.Canceled
            or NotificationAudienceBroadcastStatus.Failed;
    }

    private static string CreateKey(Guid notificationId, Guid? tenantId)
    {
        return $"{tenantId?.ToString("N") ?? "host"}:{notificationId:N}";
    }
}
