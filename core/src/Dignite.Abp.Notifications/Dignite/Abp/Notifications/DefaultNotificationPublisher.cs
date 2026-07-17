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

    public virtual async Task PublishAsync(
        string notificationName,
        NotificationData? data = null,
        NotificationEntityIdentifier? entityIdentifier = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null)
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
            await Distributor.DistributeAsync(notification, normalizedUserIds, excludedUserIds);
        }
        else
        {
            await BackgroundJobManager.EnqueueAsync(
                new NotificationDistributionJobArgs(notification, normalizedUserIds, excludedUserIds));
        }
    }
}
