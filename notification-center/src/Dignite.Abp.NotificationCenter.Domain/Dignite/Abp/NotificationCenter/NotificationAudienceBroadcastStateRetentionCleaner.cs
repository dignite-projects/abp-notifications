using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Deletes expired terminal audience-broadcast business state under Notification Center retention.</summary>
public class NotificationAudienceBroadcastStateRetentionCleaner : ITransientDependency
{
    protected IRepository<NotificationAudienceBroadcastState, Guid> StateRepository { get; }
    protected IAsyncQueryableExecuter AsyncExecuter { get; }
    protected IDataFilter DataFilter { get; }
    protected IUnitOfWorkManager UnitOfWorkManager { get; }
    protected IOptions<NotificationRetentionOptions> Options { get; }
    protected ILogger<NotificationAudienceBroadcastStateRetentionCleaner> Logger { get; }

    public NotificationAudienceBroadcastStateRetentionCleaner(
        IRepository<NotificationAudienceBroadcastState, Guid> stateRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<NotificationRetentionOptions> options,
        ILogger<NotificationAudienceBroadcastStateRetentionCleaner> logger)
    {
        StateRepository = stateRepository;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
        UnitOfWorkManager = unitOfWorkManager;
        Options = options;
        Logger = logger;
    }

    public virtual async Task CleanupAsync(
        NotificationRetentionCleanupRequest request,
        NotificationRetentionCleanupResult result,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (!Options.Value.TerminalAudienceBroadcastRetention.HasValue)
        {
            return;
        }

        var cutoff = now - Options.Value.TerminalAudienceBroadcastRetention.Value;
        NotificationAudienceBroadcastState[] candidates;
        using (var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = await StateRepository.GetQueryableAsync();
            query = query.Where(state =>
                (state.Status == NotificationAudienceBroadcastStatus.Completed ||
                 state.Status == NotificationAudienceBroadcastStatus.Canceled) &&
                state.CompletionTime.HasValue &&
                state.CompletionTime <= cutoff);
            if (request.IsTenantScoped)
            {
                query = query.Where(state => state.TenantId == request.TenantId);
            }

            candidates = (await AsyncExecuter.ToListAsync(
                query.OrderBy(state => state.CompletionTime)
                    .ThenBy(state => state.Id)
                    .Take(batchSize),
                cancellationToken)).ToArray();
            await unitOfWork.CompleteAsync(cancellationToken);
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.ScannedAudienceBroadcastStates++;
            try
            {
                using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
                using (DataFilter.Disable<IMultiTenant>())
                {
                    var entity = await StateRepository.FirstOrDefaultAsync(
                        state => state.Id == candidate.Id && state.TenantId == candidate.TenantId,
                        cancellationToken: cancellationToken);
                    if (entity == null ||
                        !NotificationAudienceBroadcastState.IsTerminal(entity.Status) ||
                        !entity.CompletionTime.HasValue ||
                        entity.CompletionTime > cutoff)
                    {
                        result.SkippedAudienceBroadcastStates++;
                    }
                    else if (request.IsDryRun)
                    {
                        result.DeletedAudienceBroadcastStates++;
                    }
                    else
                    {
                        await StateRepository.DeleteAsync(entity, autoSave: true, cancellationToken: cancellationToken);
                        result.DeletedAudienceBroadcastStates++;
                    }

                    await unitOfWork.CompleteAsync(cancellationToken);
                }
            }
            catch (AbpDbConcurrencyException)
            {
                result.SkippedAudienceBroadcastStates++;
            }
            catch (Exception exception) when (
                !cancellationToken.IsCancellationRequested &&
                exception is not OperationCanceledException)
            {
                result.AudienceBroadcastStateErrors++;
                Logger.LogWarning(
                    exception,
                    "Retention cleanup failed for audience broadcast state {NotificationId}.",
                    candidate.Id);
            }
        }
    }
}
