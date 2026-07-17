using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationPublisher : INotificationPublisher, ITransientDependency
{
    protected NotificationOptions Options { get; }

    protected INotificationDistributor Distributor { get; }

    protected IBackgroundJobManager BackgroundJobManager { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected IClock Clock { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected INotificationDataTypeRegistry DataTypeRegistry { get; }

    protected INotificationStore? Store { get; }

    public DefaultNotificationPublisher(
        IOptions<NotificationOptions> options,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant,
        INotificationDefinitionManager definitionManager,
        INotificationDataTypeRegistry dataTypeRegistry)
        : this(
            options,
            distributor,
            backgroundJobManager,
            guidGenerator,
            clock,
            currentTenant,
            definitionManager,
            dataTypeRegistry,
            store: null)
    {
    }

    public DefaultNotificationPublisher(
        IOptions<NotificationOptions> options,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant,
        INotificationDefinitionManager definitionManager,
        INotificationDataTypeRegistry dataTypeRegistry,
        INotificationStore? store)
    {
        Options = options.Value;
        Distributor = distributor;
        BackgroundJobManager = backgroundJobManager;
        GuidGenerator = guidGenerator;
        Clock = clock;
        CurrentTenant = currentTenant;
        DefinitionManager = definitionManager;
        DataTypeRegistry = dataTypeRegistry;
        Store = store;
    }

    public virtual Task PublishAsync(
        string notificationName,
        NotificationData? data = null,
        NotificationEntityIdentifier? entityIdentifier = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null)
    {
        return PublishAsyncInternal(
            notificationName,
            data,
            entityIdentifier,
            severity,
            userIds,
            excludedUserIds,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);
    }

    public virtual Task PublishToExplicitRecipientsWithoutEligibilityChecksAsync(
        string notificationName,
        Guid[] userIds,
        NotificationData? data = null,
        NotificationEntityIdentifier? entityIdentifier = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid[]? excludedUserIds = null)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        return PublishAsyncInternal(
            notificationName,
            data,
            entityIdentifier,
            severity,
            userIds,
            excludedUserIds,
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements);
    }

    protected virtual async Task PublishAsyncInternal(
        string notificationName,
        NotificationData? data,
        NotificationEntityIdentifier? entityIdentifier,
        NotificationSeverity severity,
        Guid[]? userIds,
        Guid[]? excludedUserIds,
        NotificationRecipientEligibilityMode recipientEligibilityMode)
    {
        var normalizedUserIds = userIds?.Distinct().ToArray();
        if (normalizedUserIds is { Length: 0 })
        {
            return;
        }

        var definition = DefinitionManager.Get(notificationName);
        NotificationDefinitionContractValidator.ValidatePublish(
            definition,
            data,
            entityIdentifier,
            DataTypeRegistry);

        var notification = new NotificationInfo
        {
            Id = GuidGenerator.Create(),
            NotificationName = notificationName,
            Data = data,
            EntityTypeName = entityIdentifier?.EntityTypeName,
            EntityId = entityIdentifier?.EntityId,
            Severity = severity,
            CreationTime = Clock.Now,
            TenantId = CurrentTenant.Id
        };

        if (normalizedUserIds != null && normalizedUserIds.Length <= Options.DirectDistributionUserThreshold)
        {
            if (recipientEligibilityMode == NotificationRecipientEligibilityMode.BypassDefinitionRequirements)
            {
                await Distributor.DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
                    notification,
                    normalizedUserIds,
                    excludedUserIds);
            }
            else
            {
                await Distributor.DistributeAsync(notification, normalizedUserIds, excludedUserIds);
            }
        }
        else if (normalizedUserIds != null &&
                 Store is IBatchedNotificationStore batchedStore &&
                 Distributor is IPreparedNotificationDistributor
                 {
                     SupportsPreparedDistribution: true
                 })
        {
            var excluded = excludedUserIds is { Length: > 0 }
                ? new HashSet<Guid>(excludedUserIds)
                : null;
            var recipientBatches = normalizedUserIds
                .Where(userId => excluded == null || !excluded.Contains(userId))
                .Chunk(Options.RecipientBatchSize);
            var notificationPrepared = false;
            foreach (var recipientBatch in recipientBatches)
            {
                if (!notificationPrepared)
                {
                    await batchedStore.InsertNotificationAsync(notification, CancellationToken.None);
                    notificationPrepared = true;
                }

                await BackgroundJobManager.EnqueueAsync(
                    new NotificationDistributionJobArgs(
                        notification,
                        recipientBatch,
                        excludedUserIds: null,
                        recipientEligibilityMode,
                        notificationAlreadyPersisted: true));
            }
        }
        else
        {
            await BackgroundJobManager.EnqueueAsync(
                new NotificationDistributionJobArgs(
                    notification,
                    normalizedUserIds,
                    excludedUserIds,
                    recipientEligibilityMode));
        }
    }
}
