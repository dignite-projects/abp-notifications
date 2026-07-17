using System;
using System.Linq;
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

    public DefaultNotificationPublisher(
        IOptions<NotificationOptions> options,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant)
    {
        Options = options.Value;
        Distributor = distributor;
        BackgroundJobManager = backgroundJobManager;
        GuidGenerator = guidGenerator;
        Clock = clock;
        CurrentTenant = currentTenant;
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
