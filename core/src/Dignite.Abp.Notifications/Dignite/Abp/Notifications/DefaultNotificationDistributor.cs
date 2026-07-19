using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationDistributor :
    INotificationDistributor,
    ITransientDependency
{
    protected INotificationStore Store { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected IDistributedEventBus DistributedEventBus { get; }

    protected INotificationRecipientEligibilityEvaluator RecipientEligibilityEvaluator { get; }

    protected INotificationDeliveryPreferenceEvaluator DeliveryPreferenceEvaluator { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected ILogger<DefaultNotificationDistributor> Logger { get; }

    protected INotificationDataTypeRegistry DataTypeRegistry { get; }

    protected NotificationDistributionOptions Options { get; }

    /// <summary>
    /// Source-compatible constructor for applications/tests that instantiate the default distributor directly.
    /// Dependency injection uses the options-aware overload below.
    /// </summary>
    /// <remarks>
    /// This overload pins the always-deliver <see cref="AllowAllNotificationDeliveryPreferenceEvaluator"/>: user
    /// delivery preferences and quiet hours are NOT evaluated. Resolve the distributor from DI, or pass an
    /// <see cref="INotificationDeliveryPreferenceEvaluator"/> explicitly, to honor them.
    /// </remarks>
    public DefaultNotificationDistributor(
        INotificationStore store,
        INotificationDefinitionManager definitionManager,
        IDistributedEventBus distributedEventBus,
        INotificationRecipientEligibilityEvaluator recipientEligibilityEvaluator,
        ICurrentTenant currentTenant,
        ILogger<DefaultNotificationDistributor> logger,
        INotificationDataTypeRegistry dataTypeRegistry)
        : this(
            store,
            definitionManager,
            distributedEventBus,
            recipientEligibilityEvaluator,
            currentTenant,
            logger,
            dataTypeRegistry,
            new AllowAllNotificationDeliveryPreferenceEvaluator(),
            Microsoft.Extensions.Options.Options.Create(new NotificationDistributionOptions()))
    {
    }

    /// <remarks>
    /// This overload pins the always-deliver <see cref="AllowAllNotificationDeliveryPreferenceEvaluator"/>: user
    /// delivery preferences and quiet hours are NOT evaluated. Resolve the distributor from DI, or pass an
    /// <see cref="INotificationDeliveryPreferenceEvaluator"/> explicitly, to honor them.
    /// </remarks>
    public DefaultNotificationDistributor(
        INotificationStore store,
        INotificationDefinitionManager definitionManager,
        IDistributedEventBus distributedEventBus,
        INotificationRecipientEligibilityEvaluator recipientEligibilityEvaluator,
        ICurrentTenant currentTenant,
        ILogger<DefaultNotificationDistributor> logger,
        INotificationDataTypeRegistry dataTypeRegistry,
        IOptions<NotificationDistributionOptions> options)
        : this(
            store,
            definitionManager,
            distributedEventBus,
            recipientEligibilityEvaluator,
            currentTenant,
            logger,
            dataTypeRegistry,
            new AllowAllNotificationDeliveryPreferenceEvaluator(),
            options)
    {
    }

    public DefaultNotificationDistributor(
        INotificationStore store,
        INotificationDefinitionManager definitionManager,
        IDistributedEventBus distributedEventBus,
        INotificationRecipientEligibilityEvaluator recipientEligibilityEvaluator,
        ICurrentTenant currentTenant,
        ILogger<DefaultNotificationDistributor> logger,
        INotificationDataTypeRegistry dataTypeRegistry,
        INotificationDeliveryPreferenceEvaluator deliveryPreferenceEvaluator,
        IOptions<NotificationDistributionOptions> options)
    {
        Store = store;
        DefinitionManager = definitionManager;
        DistributedEventBus = distributedEventBus;
        RecipientEligibilityEvaluator = recipientEligibilityEvaluator;
        DeliveryPreferenceEvaluator = deliveryPreferenceEvaluator;
        CurrentTenant = currentTenant;
        Logger = logger;
        DataTypeRegistry = dataTypeRegistry;
        Options = options.Value;
        Options.Validate();
    }

    public virtual Task DistributeAsync(
        NotificationInfo notification,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null,
        CancellationToken cancellationToken = default)
    {
        return DistributeAsyncInternal(
            notification,
            userIds,
            excludedUserIds,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements,
            notificationAlreadyPersisted: false,
            cancellationToken);
    }

    public virtual Task DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
        NotificationInfo notification,
        Guid[] userIds,
        Guid[]? excludedUserIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        return DistributeAsyncInternal(
            notification,
            userIds,
            excludedUserIds,
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements,
            notificationAlreadyPersisted: false,
            cancellationToken);
    }

    public virtual Task DistributePreparedAsync(
        NotificationInfo notification,
        Guid[] userIds,
        NotificationRecipientEligibilityMode recipientEligibilityMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        return DistributeAsyncInternal(
            notification,
            userIds,
            excludedUserIds: null,
            recipientEligibilityMode,
            notificationAlreadyPersisted: true,
            cancellationToken);
    }

    protected virtual async Task DistributeAsyncInternal(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds,
        NotificationRecipientEligibilityMode recipientEligibilityMode,
        bool notificationAlreadyPersisted,
        CancellationToken cancellationToken)
    {
        // An empty, explicitly supplied recipient list is intentionally different from null. Return before
        // subscription lookup or channel validation so every direct/background path remains a true no-op.
        if (userIds is { Length: 0 })
        {
            return;
        }

        var source = userIds == null ? "subscription" : "explicit";
        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";
        var stage = "validation";
        var state = new DistributionState(
            Options.DeliveryWorkItemBatchSize,
            notificationAlreadyPersisted);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = DefinitionManager.Get(notification.NotificationName);
            NotificationDefinitionContractValidator.ValidateDistribution(
                definition,
                notification,
                DataTypeRegistry);

            using (CurrentTenant.Change(notification.TenantId, null))
            {
                if (recipientEligibilityMode == NotificationRecipientEligibilityMode.BypassDefinitionRequirements)
                {
                    if (userIds == null)
                    {
                        throw new ArgumentException(
                            "Definition requirements can only be bypassed for explicit recipients.",
                            nameof(userIds));
                    }

                    // Audit outside the replaceable evaluator so a custom policy cannot make a trusted-system bypass
                    // invisible. The bounded pipeline performs the actual distinct-recipient normalization.
                    Logger.LogWarning(
                        "Bypassing notification definition requirements for {ProvidedRecipientCount} explicit " +
                        "recipient entries of '{NotificationName}' ({NotificationId}) in tenant {TenantId}.",
                        userIds.Length,
                        notification.NotificationName,
                        notification.Id,
                        notification.TenantId);
                }
                else if (recipientEligibilityMode !=
                         NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(recipientEligibilityMode),
                        recipientEligibilityMode,
                        "Unknown recipient eligibility mode.");
                }

                var channels = ResolveExternalChannelsOrNull(notification.NotificationName);
                stage = "candidate_resolution";
                if (userIds != null)
                {
                    foreach (var batch in BoundedRecipientBatcher.GetDistinctBatches(
                                 userIds,
                                 Options.RecipientBatchSize,
                                 cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await ProcessCandidateBatchAsync(
                            notification,
                            batch,
                            excludedUserIds,
                            channels,
                            source,
                            recipientEligibilityMode,
                            state,
                            cancellationToken,
                            currentStage => stage = currentStage);
                    }
                }
                else
                {
                    Guid? afterUserId = null;
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var batch = await GetSubscriptionUserIdsAsync(
                            notification,
                            afterUserId,
                            Options.RecipientBatchSize,
                            cancellationToken);
                        if (batch.Count == 0)
                        {
                            break;
                        }

                        afterUserId = batch[^1];
                        await ProcessCandidateBatchAsync(
                            notification,
                            batch,
                            excludedUserIds,
                            channels,
                            source,
                            recipientEligibilityMode,
                            state,
                            cancellationToken,
                            currentStage => stage = currentStage);

                        if (batch.Count < Options.RecipientBatchSize)
                        {
                            break;
                        }
                    }
                }

                if (channels != null && state.DeliveryBuffer.Count > 0)
                {
                    stage = "delivery";
                    await PublishBufferedDeliveryAsync(
                        notification,
                        channels,
                        source,
                        state,
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            outcome = "canceled";
            Logger.LogInformation(
                "Notification distribution for '{NotificationName}' ({NotificationId}) was canceled during {Stage} after " +
                "{CandidateCount} candidates, {EligibleCount} eligible recipients, {FilteredCount} filtered " +
                "recipients, and {BatchCount} completed/attempted batches.",
                notification.NotificationName,
                notification.Id,
                stage,
                state.CandidateCount,
                state.EligibleCount,
                state.FilteredCount,
                state.TotalBatchCount);
            throw;
        }
        catch (Exception exception)
        {
            outcome = "failure";
            NotificationDistributionMetrics.FailureCount.Add(
                1,
                CreateTags(notification, source, stage: stage));
            Logger.LogError(
                exception,
                "Notification distribution for '{NotificationName}' ({NotificationId}) failed during {Stage} after " +
                "{CandidateCount} candidates, {EligibleCount} eligible recipients, {FilteredCount} filtered " +
                "recipients, and {BatchCount} completed/attempted batches.",
                notification.NotificationName,
                notification.Id,
                stage,
                state.CandidateCount,
                state.EligibleCount,
                state.FilteredCount,
                state.TotalBatchCount);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            NotificationDistributionMetrics.Duration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                CreateTags(notification, source, outcome: outcome));

            Logger.LogInformation(
                "Notification distribution for '{NotificationName}' ({NotificationId}) finished with outcome {Outcome}: " +
                "{CandidateCount} candidates, {EligibleCount} eligible recipients, {FilteredCount} filtered " +
                "recipients, {CandidateBatchCount} candidate batches, {PersistenceBatchCount} persistence " +
                "batches, {DeliveryBatchCount} delivery batches, {DurationMilliseconds} ms.",
                notification.NotificationName,
                notification.Id,
                outcome,
                state.CandidateCount,
                state.EligibleCount,
                state.FilteredCount,
                state.CandidateBatchCount,
                state.PersistenceBatchCount,
                state.DeliveryBatchCount,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task ProcessCandidateBatchAsync(
        NotificationInfo notification,
        IReadOnlyCollection<Guid> resolvedCandidates,
        Guid[]? excludedUserIds,
        string[]? channels,
        string source,
        NotificationRecipientEligibilityMode recipientEligibilityMode,
        DistributionState state,
        CancellationToken cancellationToken,
        Action<string> setStage)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.CandidateBatchCount++;
        state.CandidateCount += resolvedCandidates.Count;
        NotificationDistributionMetrics.CandidateCount.Add(
            resolvedCandidates.Count,
            CreateTags(notification, source));
        NotificationDistributionMetrics.BatchCount.Add(
            1,
            CreateTags(notification, source, stage: "candidate"));

        var candidates = BoundedRecipientBatcher.RemoveExcludedRecipients(
                resolvedCandidates.ToArray(),
                excludedUserIds)
            .ToList();
        var callerFilteredCount = resolvedCandidates.Count - candidates.Count;
        if (callerFilteredCount > 0)
        {
            state.FilteredCount += callerFilteredCount;
            NotificationDistributionMetrics.FilteredCount.Add(
                callerFilteredCount,
                CreateTags(notification, source, stage: "caller_exclusion"));
        }

        if (candidates.Count == 0)
        {
            LogBatchProgress(notification, state);
            return;
        }

        setStage("eligibility");
        cancellationToken.ThrowIfCancellationRequested();
        // Eligibility deliberately runs after candidate selection so custom policies cannot restore the historical
        // explicit-recipient bypass. Each invocation is bounded by RecipientBatchSize.
        var evaluation = await RecipientEligibilityEvaluator.EvaluateAsync(
            notification.NotificationName,
            candidates,
            notification.TenantId,
            recipientEligibilityMode,
            cancellationToken);

        // Do not let a replaceable evaluator inject users that were not in this bounded candidate batch.
        var candidateSet = new HashSet<Guid>(candidates);
        var eligible = evaluation.EligibleUserIds
            .Where(candidateSet.Contains)
            .Distinct()
            .ToList();
        var policyFilteredCount = candidates.Count - eligible.Count;
        state.EligibleCount += eligible.Count;
        state.FilteredCount += policyFilteredCount;
        NotificationDistributionMetrics.EligibleCount.Add(
            eligible.Count,
            CreateTags(notification, source));
        if (policyFilteredCount > 0)
        {
            NotificationDistributionMetrics.FilteredCount.Add(
                policyFilteredCount,
                CreateTags(notification, source, stage: "eligibility"));
        }

        if (eligible.Count == 0)
        {
            LogBatchProgress(notification, state);
            return;
        }

        foreach (var writeBatch in eligible.Chunk(Options.UserNotificationWriteBatchSize))
        {
            setStage("persistence");
            cancellationToken.ThrowIfCancellationRequested();
            if (!state.NotificationInserted)
            {
                await InsertNotificationAsync(notification, cancellationToken);
                state.NotificationInserted = true;
            }

            var inboxRows = writeBatch.Select(userId => new UserNotificationInfo
            {
                UserId = userId,
                NotificationId = notification.Id,
                State = UserNotificationState.Unread,
                CreationTime = notification.CreationTime,
                TenantId = notification.TenantId
            }).ToList();

            state.PersistenceBatchCount++;
            NotificationDistributionMetrics.BatchCount.Add(
                1,
                CreateTags(notification, source, stage: "persistence"));
            await InsertUserNotificationsAsync(inboxRows, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (channels == null)
            {
                continue;
            }

            foreach (var userId in writeBatch)
            {
                state.DeliveryBuffer.Add(userId);
                if (state.DeliveryBuffer.Count == Options.DeliveryWorkItemBatchSize)
                {
                    setStage("delivery");
                    await PublishBufferedDeliveryAsync(
                        notification,
                        channels,
                        source,
                        state,
                        cancellationToken);
                }
            }
        }

        LogBatchProgress(notification, state);
    }

    private async Task PublishBufferedDeliveryAsync(
        NotificationInfo notification,
        string[] channels,
        string source,
        DistributionState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.DeliveryBatchCount++;
        NotificationDistributionMetrics.BatchCount.Add(
            1,
            CreateTags(notification, source, stage: "delivery"));
        await PublishNotificationDeliveryBatchAsync(
            notification,
            state.DeliveryBuffer,
            channels,
            cancellationToken);
        state.DeliveryBuffer.Clear();
        cancellationToken.ThrowIfCancellationRequested();
    }

    protected virtual string[]? ResolveExternalChannelsOrNull(string notificationName)
    {
        var definition = DefinitionManager.Get(notificationName);

        var channels = definition.GetChannelsOrNull();
        if (channels == null)
        {
            if (Store is NullNotificationStore)
            {
                throw new AbpException(
                    $"Notification '{notificationName}' has no external channels and no NotificationCenter inbox store is installed. Configure UseChannels(...) or install NotificationCenter.");
            }

            return null;
        }

        if (channels.Length == 0 || channels.Any(string.IsNullOrWhiteSpace))
        {
            throw new AbpException(
                $"Notification '{notificationName}' has invalid delivery channel configuration.");
        }

        return channels;
    }

    private async Task<List<Guid>> GetSubscriptionUserIdsAsync(
        NotificationInfo notification,
        Guid? afterUserId,
        int maxResultCount,
        CancellationToken cancellationToken)
    {
        return await Store.GetSubscriptionUserIdsAsync(
            notification.NotificationName,
            notification.EntityTypeName,
            notification.EntityId,
            afterUserId,
            maxResultCount,
            cancellationToken);
    }

    private async Task InsertNotificationAsync(
        NotificationInfo notification,
        CancellationToken cancellationToken)
    {
        await Store.InsertNotificationAsync(notification, cancellationToken);
    }

    private async Task InsertUserNotificationsAsync(
        IReadOnlyCollection<UserNotificationInfo> userNotifications,
        CancellationToken cancellationToken)
    {
        await Store.InsertUserNotificationsAsync(userNotifications, cancellationToken);
    }

    /// <summary>
    /// Converts an already-bounded recipient batch into independently claimable recipient/channel work items.
    /// </summary>
    protected virtual async Task PublishNotificationDeliveryBatchAsync(
        NotificationInfo notification,
        IReadOnlyCollection<Guid> userIds,
        string[] channels,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = userIds
            .Distinct()
            .SelectMany(userId => channels
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(channel => new NotificationDeliveryPreferenceCandidate(userId, channel)))
            .ToList();
        var definition = DefinitionManager.Get(notification.NotificationName);
        var decisions = await DeliveryPreferenceEvaluator.EvaluateAsync(
            notification.NotificationName,
            notification.TenantId,
            candidates,
            definition.DeliveryPreferenceBehavior,
            cancellationToken);
        var decisionMap = new Dictionary<(Guid UserId, string Channel), NotificationDeliveryPreferenceDecision>();
        foreach (var decision in decisions)
        {
            var key = (decision.UserId, NotificationDeliveryIdentity.NormalizeChannel(decision.Channel));
            if (!decisionMap.TryAdd(key, decision))
            {
                throw new InvalidOperationException(
                    $"The delivery preference evaluator returned duplicate decisions for user '{decision.UserId}' " +
                    $"and channel '{decision.Channel}'.");
            }
        }

        if (decisionMap.Count != candidates.Count)
        {
            throw new InvalidOperationException(
                "The delivery preference evaluator must return exactly one decision for every recipient/channel candidate.");
        }

        var workItems = new List<NotificationDeliveryRequestedEto>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (!decisionMap.TryGetValue((
                    candidate.UserId,
                    NotificationDeliveryIdentity.NormalizeChannel(candidate.Channel)), out var decision))
            {
                throw new InvalidOperationException(
                    $"The delivery preference evaluator returned no decision for user '{candidate.UserId}' " +
                    $"and channel '{candidate.Channel}'.");
            }

            var workItem = CreateDeliveryWorkItem(notification, candidate.UserId, candidate.Channel);
            workItem.Intent = decision.Intent;
            workItem.DeliveryNotBefore = decision.NotBefore;
            workItem.PreferenceReasonCode = decision.ReasonCode;
            workItem.ValidateIntent();
            workItems.Add(workItem);
        }

        foreach (var workItem in workItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DistributedEventBus.PublishAsync(workItem);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    protected virtual NotificationDeliveryRequestedEto CreateDeliveryWorkItem(
        NotificationInfo notification,
        Guid userId,
        string channel)
    {
        return new NotificationDeliveryRequestedEto
        {
            DeliveryId = NotificationDeliveryIdentity.CreateId(
                notification.TenantId,
                notification.Id,
                userId,
                channel),
            IdempotencyKey = NotificationDeliveryIdentity.CreateIdempotencyKey(
                notification.TenantId,
                notification.Id,
                userId,
                channel),
            NotificationId = notification.Id,
            NotificationName = notification.NotificationName,
            Data = notification.Data,
            Severity = notification.Severity,
            CreationTime = notification.CreationTime,
            UserId = userId,
            Channel = channel,
            TenantId = notification.TenantId,
            EntityTypeName = notification.EntityTypeName,
            EntityId = notification.EntityId
        };
    }

    private void LogBatchProgress(NotificationInfo notification, DistributionState state)
    {
        Logger.LogDebug(
            "Notification '{NotificationName}' ({NotificationId}) distribution progress: {CandidateCount} candidates, " +
            "{EligibleCount} eligible recipients, {FilteredCount} filtered recipients, {CandidateBatchCount} " +
            "candidate batches, {PersistenceBatchCount} persistence batches, {DeliveryBatchCount} delivery batches.",
            notification.NotificationName,
            notification.Id,
            state.CandidateCount,
            state.EligibleCount,
            state.FilteredCount,
            state.CandidateBatchCount,
            state.PersistenceBatchCount,
            state.DeliveryBatchCount);
    }

    private static TagList CreateTags(
        NotificationInfo notification,
        string source,
        string? stage = null,
        string? outcome = null)
    {
        var tags = new TagList
        {
            { "notification.name", notification.NotificationName },
            { "recipient.source", source },
            { "tenant.scope", notification.TenantId.HasValue ? "tenant" : "host" }
        };
        if (stage != null)
        {
            tags.Add("distribution.stage", stage);
        }

        if (outcome != null)
        {
            tags.Add("distribution.outcome", outcome);
        }

        return tags;
    }

    private sealed class DistributionState
    {
        public long CandidateCount { get; set; }

        public long EligibleCount { get; set; }

        public long FilteredCount { get; set; }

        public long CandidateBatchCount { get; set; }

        public long PersistenceBatchCount { get; set; }

        public long DeliveryBatchCount { get; set; }

        public bool NotificationInserted { get; set; }

        public List<Guid> DeliveryBuffer { get; }

        public long TotalBatchCount =>
            CandidateBatchCount + PersistenceBatchCount + DeliveryBatchCount;

        public DistributionState(
            int deliveryEventRecipientLimit,
            bool notificationInserted)
        {
            DeliveryBuffer = new List<Guid>(deliveryEventRecipientLimit);
            NotificationInserted = notificationInserted;
        }
    }
}
