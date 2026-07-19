using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Process-local audience-broadcast workflow state for Core-only and single-instance diagnostics. This store is not
/// durable and is not shared across application instances; Notification Center replaces it with persistent state.
/// </summary>
[ExposeServices(typeof(INotificationAudienceBroadcastProgressStore))]
public class InMemoryNotificationAudienceBroadcastProgressStore :
    INotificationAudienceBroadcastProgressStore,
    ISingletonDependency
{
    private readonly ConcurrentDictionary<string, NotificationAudienceBroadcastProgress> _progresses = new();
    private readonly object _sync = new();

    public virtual Task<NotificationAudienceBroadcastProgress?> GetAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult(
                _progresses.TryGetValue(CreateKey(notificationId, tenantId), out var progress)
                    ? progress.Clone()
                    : null);
        }
    }

    public virtual Task RecordStartedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
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
                    existing.Status = existing.IsCancellationRequested
                        ? NotificationAudienceBroadcastStatus.CancellationRequested
                        : existing.Status == NotificationAudienceBroadcastStatus.Failed
                            ? NotificationAudienceBroadcastStatus.Enqueued
                            : existing.Status;
                    existing.FailureCode = null;
                    existing.FailureMessage = null;
                    existing.CompletionTime = null;
                    Touch(existing, updateTime);
                    return existing;
                });
        }

        return Task.CompletedTask;
    }

    public virtual Task RecordPageCompletedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        long pageIndex,
        long candidateCount,
        string? nextContinuationToken,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
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
                    if (pageIndex == 0)
                    {
                        progress.CompletedPageCount = 1;
                        progress.CandidateCount = candidateCount;
                        progress.NextContinuationToken = nextContinuationToken;
                    }

                    return progress;
                },
                (_, existing) =>
                {
                    if (IsTerminal(existing.Status) || pageIndex != existing.CompletedPageCount)
                    {
                        return existing;
                    }

                    existing.CandidateCount += candidateCount;
                    existing.CompletedPageCount++;
                    existing.Status = existing.IsCancellationRequested
                        ? NotificationAudienceBroadcastStatus.CancellationRequested
                        : NotificationAudienceBroadcastStatus.Running;
                    existing.NextContinuationToken = nextContinuationToken;
                    existing.FailureCode = null;
                    existing.FailureMessage = null;
                    Touch(existing, updateTime);
                    return existing;
                });
        }

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
        lock (_sync)
        {
            _progresses.AddOrUpdate(
                CreateKey(notification.Id, tenantId),
                _ => NewTerminalProgress(
                    notification,
                    audienceName,
                    tenantId,
                    NotificationAudienceBroadcastStatus.Completed,
                    updateTime),
                (_, existing) =>
                {
                    if (IsTerminal(existing.Status))
                    {
                        return existing;
                    }

                    existing.Status = existing.IsCancellationRequested
                        ? NotificationAudienceBroadcastStatus.Canceled
                        : NotificationAudienceBroadcastStatus.Completed;
                    existing.NextContinuationToken = null;
                    existing.CompletionTime = updateTime;
                    existing.FailureCode = null;
                    existing.FailureMessage = null;
                    Touch(existing, updateTime);
                    return existing;
                });
        }

        return Task.CompletedTask;
    }

    public virtual Task<bool> RequestCancellationAsync(
        Guid notificationId,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_progresses.TryGetValue(CreateKey(notificationId, tenantId), out var existing) ||
                IsTerminal(existing.Status))
            {
                return Task.FromResult(false);
            }

            existing.IsCancellationRequested = true;
            existing.CancellationRequestedTime ??= updateTime;
            existing.Status = NotificationAudienceBroadcastStatus.CancellationRequested;
            Touch(existing, updateTime);
            return Task.FromResult(true);
        }
    }

    public virtual Task<bool> IsCancellationRequestedAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            return Task.FromResult(
                _progresses.TryGetValue(CreateKey(notificationId, tenantId), out var existing) &&
                existing.IsCancellationRequested);
        }
    }

    public virtual Task RecordCanceledAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _progresses.AddOrUpdate(
                CreateKey(notification.Id, tenantId),
                _ => NewTerminalProgress(
                    notification,
                    audienceName,
                    tenantId,
                    NotificationAudienceBroadcastStatus.Canceled,
                    updateTime,
                    cancellationRequested: true),
                (_, existing) =>
                {
                    if (IsTerminal(existing.Status))
                    {
                        return existing;
                    }

                    existing.Status = NotificationAudienceBroadcastStatus.Canceled;
                    existing.IsCancellationRequested = true;
                    existing.CancellationRequestedTime ??= updateTime;
                    existing.NextContinuationToken = null;
                    existing.CompletionTime = updateTime;
                    Touch(existing, updateTime);
                    return existing;
                });
        }

        return Task.CompletedTask;
    }

    public virtual Task RecordFailedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        string failureCode,
        string failureMessage,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
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
                    progress.FailureCode = failureCode;
                    progress.FailureMessage = failureMessage;
                    return progress;
                },
                (_, existing) =>
                {
                    if (IsTerminal(existing.Status))
                    {
                        return existing;
                    }

                    existing.Status = NotificationAudienceBroadcastStatus.Failed;
                    existing.FailureCode = failureCode;
                    existing.FailureMessage = failureMessage;
                    Touch(existing, updateTime);
                    return existing;
                });
        }

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
            CreationTime = updateTime,
            LastUpdatedTime = updateTime,
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
    }

    private NotificationAudienceBroadcastProgress NewTerminalProgress(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        NotificationAudienceBroadcastStatus status,
        DateTime updateTime,
        bool cancellationRequested = false)
    {
        var progress = NewProgress(notification, audienceName, tenantId, status, updateTime);
        progress.CompletionTime = updateTime;
        progress.IsCancellationRequested = cancellationRequested;
        progress.CancellationRequestedTime = cancellationRequested ? updateTime : null;
        return progress;
    }

    private static void Touch(NotificationAudienceBroadcastProgress progress, DateTime updateTime)
    {
        progress.LastUpdatedTime = updateTime;
        progress.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    private static bool IsTerminal(NotificationAudienceBroadcastStatus status)
    {
        return status is NotificationAudienceBroadcastStatus.Completed
            or NotificationAudienceBroadcastStatus.Canceled;
    }

    private static string CreateKey(Guid notificationId, Guid? tenantId)
    {
        return $"{tenantId?.ToString("N") ?? "host"}:{notificationId:N}";
    }
}
