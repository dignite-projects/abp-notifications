using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Durable EF Core/MongoDB implementation of Notifications-owned audience-broadcast workflow state.
/// ABP remains responsible for background-job persistence, scheduling, retries, and clustered execution.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(
    typeof(INotificationAudienceBroadcastProgressStore),
    typeof(NotificationAudienceBroadcastProgressStore))]
public class NotificationAudienceBroadcastProgressStore :
    INotificationAudienceBroadcastProgressStore,
    ITransientDependency
{
    private const int MaxConcurrencyAttempts = 5;

    protected IRepository<NotificationAudienceBroadcastState, Guid> StateRepository { get; }

    protected IDataFilter DataFilter { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    public NotificationAudienceBroadcastProgressStore(
        IRepository<NotificationAudienceBroadcastState, Guid> stateRepository,
        IDataFilter dataFilter,
        IUnitOfWorkManager unitOfWorkManager)
    {
        StateRepository = stateRepository;
        DataFilter = dataFilter;
        UnitOfWorkManager = unitOfWorkManager;
    }

    public virtual async Task<NotificationAudienceBroadcastProgress?> GetAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        ValidateNotificationId(notificationId);
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var state = await FindAsync(notificationId, tenantId, cancellationToken);
            var progress = state?.ToProgress();
            await unitOfWork.CompleteAsync(cancellationToken);
            return progress;
        }
    }

    public virtual Task RecordStartedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            notification,
            audienceName,
            tenantId,
            state => state.RecordStarted(notification.NotificationName, audienceName, updateTime),
            updateTime,
            cancellationToken);
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
        return MutateAsync(
            notification,
            audienceName,
            tenantId,
            state => state.RecordPageCompleted(
                pageIndex,
                candidateCount,
                nextContinuationToken,
                updateTime),
            updateTime,
            cancellationToken);
    }

    public virtual Task RecordCompletedAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            notification,
            audienceName,
            tenantId,
            state => state.Complete(updateTime),
            updateTime,
            cancellationToken);
    }

    public virtual Task<bool> RequestCancellationAsync(
        Guid notificationId,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        ValidateNotificationId(notificationId);
        return MutateExistingAsync(
            notificationId,
            tenantId,
            state => state.RequestCancellation(updateTime),
            cancellationToken);
    }

    public virtual async Task<bool> IsCancellationRequestedAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var progress = await GetAsync(notificationId, tenantId, cancellationToken);
        return progress?.IsCancellationRequested == true;
    }

    public virtual Task RecordCanceledAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        DateTime updateTime,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            notification,
            audienceName,
            tenantId,
            state => state.Cancel(updateTime),
            updateTime,
            cancellationToken);
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
        return MutateAsync(
            notification,
            audienceName,
            tenantId,
            state => state.Fail(failureCode, failureMessage, updateTime),
            updateTime,
            cancellationToken);
    }

    protected virtual async Task MutateAsync(
        NotificationInfo notification,
        string audienceName,
        Guid? tenantId,
        Func<NotificationAudienceBroadcastState, bool> transition,
        DateTime creationTime,
        CancellationToken cancellationToken)
    {
        ValidateScope(notification, tenantId);
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            var insertInProgress = false;
            try
            {
                using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
                using (DataFilter.Disable<IMultiTenant>())
                {
                    var state = await FindAsync(notification.Id, tenantId, cancellationToken);
                    if (state == null)
                    {
                        state = new NotificationAudienceBroadcastState(
                            notification.Id,
                            notification.NotificationName,
                            audienceName,
                            creationTime,
                            tenantId);
                        transition(state);
                        insertInProgress = true;
                        await StateRepository.InsertAsync(state, autoSave: true, cancellationToken: cancellationToken);
                        insertInProgress = false;
                    }
                    else if (transition(state))
                    {
                        await StateRepository.UpdateAsync(state, autoSave: true, cancellationToken: cancellationToken);
                    }

                    await unitOfWork.CompleteAsync(cancellationToken);
                    return;
                }
            }
            catch (AbpDbConcurrencyException) when (attempt < MaxConcurrencyAttempts)
            {
                // Reload in a fresh UOW and reapply the idempotent transition.
            }
            catch (Exception) when (
                insertInProgress &&
                attempt < MaxConcurrencyAttempts &&
                !cancellationToken.IsCancellationRequested)
            {
                // Provider-specific duplicate-key exceptions differ. A fresh read proves a concurrent insert won.
                if (!await ExistsAsync(notification.Id, tenantId, cancellationToken))
                {
                    throw;
                }
            }
        }
    }

    protected virtual async Task<bool> MutateExistingAsync(
        Guid notificationId,
        Guid? tenantId,
        Func<NotificationAudienceBroadcastState, bool> transition,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
                using (DataFilter.Disable<IMultiTenant>())
                {
                    var state = await FindAsync(notificationId, tenantId, cancellationToken);
                    if (state == null || !transition(state))
                    {
                        await unitOfWork.CompleteAsync(cancellationToken);
                        return false;
                    }

                    await StateRepository.UpdateAsync(state, autoSave: true, cancellationToken: cancellationToken);
                    await unitOfWork.CompleteAsync(cancellationToken);
                    return true;
                }
            }
            catch (AbpDbConcurrencyException) when (attempt < MaxConcurrencyAttempts)
            {
                // Reload in a fresh UOW and reapply the transition.
            }
        }

        return false;
    }

    protected virtual Task<NotificationAudienceBroadcastState?> FindAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        return StateRepository.FirstOrDefaultAsync(
            state => state.Id == notificationId && state.TenantId == tenantId,
            cancellationToken: cancellationToken);
    }

    protected virtual async Task<bool> ExistsAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var exists = await StateRepository.AnyAsync(
                state => state.Id == notificationId && state.TenantId == tenantId,
                cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return exists;
        }
    }

    private static void ValidateScope(NotificationInfo notification, Guid? tenantId)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ValidateNotificationId(notification.Id);
        if (notification.TenantId != tenantId)
        {
            throw new InvalidOperationException(
                "The audience-broadcast workflow tenant id must match the prepared notification tenant id.");
        }
    }

    private static void ValidateNotificationId(Guid notificationId)
    {
        if (notificationId == Guid.Empty)
        {
            throw new ArgumentException("Notification id cannot be Guid.Empty.", nameof(notificationId));
        }
    }
}
