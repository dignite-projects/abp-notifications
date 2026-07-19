using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
    ICancellableNotificationDistributor,
    IPreparedNotificationDistributor,
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

    protected NotificationOptions Options { get; }

    public virtual bool SupportsPreparedDistribution => !UsesLegacyExtensionPointOverrides();

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
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions()))
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
        IOptions<NotificationOptions> options)
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
        IOptions<NotificationOptions> options)
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
        Options.ValidateDistributionBatching();
    }

    public virtual Task DistributeAsync(
        NotificationInfo notification,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null)
    {
        return DistributeAsync(notification, userIds, excludedUserIds, CancellationToken.None);
    }

    public virtual Task DistributeAsync(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds,
        CancellationToken cancellationToken)
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
        Guid[]? excludedUserIds = null)
    {
        return DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
            notification,
            userIds,
            excludedUserIds,
            CancellationToken.None);
    }

    public virtual Task DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
        NotificationInfo notification,
        Guid[] userIds,
        Guid[]? excludedUserIds,
        CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (!SupportsPreparedDistribution)
        {
            throw new InvalidOperationException(
                "A distributor with legacy protected overrides cannot process prepared notification batches.");
        }

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
            Options.DeliveryEventRecipientLimit,
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
                if (UsesLegacyExtensionPointOverrides())
                {
                    await DistributeUsingLegacyExtensionPointsAsync(
                        notification,
                        userIds,
                        excludedUserIds,
                        channels,
                        source,
                        recipientEligibilityMode,
                        state,
                        cancellationToken,
                        currentStage => stage = currentStage);
                    return;
                }

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
            recipientEligibilityMode);
        cancellationToken.ThrowIfCancellationRequested();

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
                if (state.DeliveryBuffer.Count == Options.DeliveryEventRecipientLimit)
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
            channels);
        state.DeliveryBuffer.Clear();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task DistributeUsingLegacyExtensionPointsAsync(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds,
        string[]? channels,
        string source,
        NotificationRecipientEligibilityMode recipientEligibilityMode,
        DistributionState state,
        CancellationToken cancellationToken,
        Action<string> setStage)
    {
        Logger.LogWarning(
            "Notification distributor type {DistributorType} overrides a legacy materializing extension point. " +
            "Distribution for '{NotificationName}' ({NotificationId}) retains compatibility semantics and does not " +
            "have bounded " +
            "candidate, persistence, or delivery guarantees.",
            GetNonProxyImplementationType().FullName,
            notification.NotificationName,
            notification.Id);

        setStage("candidate_resolution");
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0618 // Compatibility path intentionally invokes the obsolete protected hooks.
        var candidates = (await GetTargetUserIdsAsync(notification, userIds, excludedUserIds))
            .Distinct()
            .ToList();
#pragma warning restore CS0618
        cancellationToken.ThrowIfCancellationRequested();
        state.CandidateBatchCount++;
        state.CandidateCount = candidates.Count;
        NotificationDistributionMetrics.CandidateCount.Add(
            candidates.Count,
            CreateTags(notification, source));
        NotificationDistributionMetrics.BatchCount.Add(
            1,
            CreateTags(notification, source, stage: "candidate"));
        if (candidates.Count == 0)
        {
            return;
        }

        setStage("eligibility");
        var evaluation = await RecipientEligibilityEvaluator.EvaluateAsync(
            notification.NotificationName,
            candidates,
            notification.TenantId,
            recipientEligibilityMode);
        cancellationToken.ThrowIfCancellationRequested();
        var candidateSet = new HashSet<Guid>(candidates);
        var eligible = evaluation.EligibleUserIds
            .Where(candidateSet.Contains)
            .Distinct()
            .ToList();
        state.EligibleCount = eligible.Count;
        state.FilteredCount = candidates.Count - eligible.Count;
        NotificationDistributionMetrics.EligibleCount.Add(
            eligible.Count,
            CreateTags(notification, source));
        NotificationDistributionMetrics.FilteredCount.Add(
            state.FilteredCount,
            CreateTags(notification, source, stage: "eligibility"));
        if (eligible.Count == 0)
        {
            return;
        }

        setStage("persistence");
        state.PersistenceBatchCount++;
        NotificationDistributionMetrics.BatchCount.Add(
            1,
            CreateTags(notification, source, stage: "persistence"));
#pragma warning disable CS0618 // Compatibility path intentionally invokes the obsolete protected hooks.
        await SaveUserNotificationsAsync(notification, eligible);
#pragma warning restore CS0618
        cancellationToken.ThrowIfCancellationRequested();
        state.NotificationInserted = true;

        if (channels == null)
        {
            return;
        }

        setStage("delivery");
        state.DeliveryBatchCount++;
        NotificationDistributionMetrics.BatchCount.Add(
            1,
            CreateTags(notification, source, stage: "delivery"));
#pragma warning disable CS0618 // Compatibility path intentionally invokes the obsolete protected hooks.
        await PublishNotificationDeliveryAsync(notification, eligible, channels);
#pragma warning restore CS0618
        cancellationToken.ThrowIfCancellationRequested();
    }

    private bool UsesLegacyExtensionPointOverrides()
    {
        var implementationType = GetNonProxyImplementationType();
        return IsLegacyOverride(
                   implementationType,
                   nameof(GetTargetUserIdsAsync),
                   typeof(NotificationInfo),
                   typeof(Guid[]),
                   typeof(Guid[])) ||
               IsLegacyOverride(
                   implementationType,
                   nameof(SaveUserNotificationsAsync),
                   typeof(NotificationInfo),
                   typeof(List<Guid>)) ||
               IsLegacyOverride(
                   implementationType,
                   nameof(PublishNotificationDeliveryAsync),
                   typeof(NotificationInfo),
                   typeof(List<Guid>),
                   typeof(string[]));
    }

    private Type GetNonProxyImplementationType()
    {
        var implementationType = GetType();
        while (implementationType.Assembly.IsDynamic && implementationType.BaseType != null)
        {
            implementationType = implementationType.BaseType;
        }

        return implementationType;
    }

    private static bool IsLegacyOverride(
        Type implementationType,
        string methodName,
        params Type[] parameterTypes)
    {
        var method = implementationType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            parameterTypes,
            modifiers: null);
        return method != null && method.DeclaringType != typeof(DefaultNotificationDistributor);
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
        if (Store is IBatchedNotificationStore batchedStore)
        {
            return await batchedStore.GetSubscriptionUserIdsAsync(
                notification.NotificationName,
                notification.EntityTypeName,
                notification.EntityId,
                afterUserId,
                maxResultCount,
                cancellationToken);
        }

        // Compatibility path for stores compiled against the original interface. It is intentionally kept out of
        // IBatchedNotificationStore's contract: large custom-store fan-outs must implement that capability.
        cancellationToken.ThrowIfCancellationRequested();
        var subscriptions = await Store.GetSubscriptionsAsync(
            notification.NotificationName,
            notification.EntityTypeName,
            notification.EntityId);
        cancellationToken.ThrowIfCancellationRequested();
        return subscriptions
            .Select(subscription => subscription.UserId)
            .Distinct()
            .OrderBy(userId => userId)
            .Where(userId => !afterUserId.HasValue || userId.CompareTo(afterUserId.Value) > 0)
            .Take(maxResultCount)
            .ToList();
    }

    private async Task InsertNotificationAsync(
        NotificationInfo notification,
        CancellationToken cancellationToken)
    {
        if (Store is IBatchedNotificationStore batchedStore)
        {
            await batchedStore.InsertNotificationAsync(notification, cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Store.InsertNotificationAsync(notification);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task InsertUserNotificationsAsync(
        IReadOnlyCollection<UserNotificationInfo> userNotifications,
        CancellationToken cancellationToken)
    {
        if (Store is IBatchedNotificationStore batchedStore)
        {
            await batchedStore.InsertUserNotificationsAsync(userNotifications, cancellationToken);
            return;
        }

        foreach (var userNotification in userNotifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Store.InsertUserNotificationAsync(userNotification);
        }
    }

    /// <summary>
    /// Compatibility extension point retained for subclasses. Overriding any legacy extension point selects the
    /// legacy materializing pipeline; migrate to the bounded store/evaluator abstractions for large fan-outs.
    /// </summary>
    [Obsolete("The bounded pipeline pages candidates through IBatchedNotificationStore.GetSubscriptionUserIdsAsync.")]
    protected virtual async Task<List<Guid>> GetTargetUserIdsAsync(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds)
    {
        List<Guid> candidates;

        if (userIds != null)
        {
            candidates = userIds.Distinct().ToList();
        }
        else
        {
            var subscriptions = await Store.GetSubscriptionsAsync(
                notification.NotificationName,
                notification.EntityTypeName,
                notification.EntityId);
            candidates = subscriptions.Select(subscription => subscription.UserId).Distinct().ToList();
        }

        if (excludedUserIds != null && excludedUserIds.Length > 0)
        {
            var excluded = new HashSet<Guid>(excludedUserIds);
            candidates = candidates.Where(userId => !excluded.Contains(userId)).ToList();
        }

        return candidates;
    }

    /// <summary>Compatibility extension point retained for subclasses; the default pipeline uses bounded writes.</summary>
    [Obsolete("The bounded pipeline calls IBatchedNotificationStore.InsertUserNotificationsAsync per configured batch.")]
    protected virtual async Task SaveUserNotificationsAsync(NotificationInfo notification, List<Guid> userIds)
    {
        await Store.InsertNotificationAsync(notification);
        await InsertUserNotificationsAsync(userIds.Select(userId => new UserNotificationInfo
        {
            UserId = userId,
            NotificationId = notification.Id,
            State = UserNotificationState.Unread,
            CreationTime = notification.CreationTime,
            TenantId = notification.TenantId
        }).ToList(), CancellationToken.None);
    }

    /// <summary>
    /// Converts an already-bounded recipient batch into independently claimable recipient/channel work items.
    /// </summary>
    protected virtual async Task PublishNotificationDeliveryBatchAsync(
        NotificationInfo notification,
        IReadOnlyCollection<Guid> userIds,
        string[] channels)
    {
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
            definition.DeliveryPreferenceBehavior);
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
            await DistributedEventBus.PublishAsync(workItem);
        }
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

    /// <summary>Compatibility extension point retained for subclasses.</summary>
    [Obsolete("Use PublishNotificationDeliveryBatchAsync; every event must respect DeliveryEventRecipientLimit.")]
    protected virtual Task PublishNotificationDeliveryAsync(
        NotificationInfo notification,
        List<Guid> userIds,
        string[] channels)
    {
        return PublishNotificationDeliveryBatchAsync(notification, userIds, channels);
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
