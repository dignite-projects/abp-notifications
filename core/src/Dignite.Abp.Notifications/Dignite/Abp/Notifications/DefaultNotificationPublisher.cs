using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationPublisher : INotificationPublisher, ITransientDependency
{
    protected NotificationOptions Options { get; }

    protected INotificationDistributor Distributor { get; }

    protected IBackgroundJobManager BackgroundJobManager { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected IClock Clock { get; }

    public DefaultNotificationPublisher(
        IOptions<NotificationOptions> options,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        Options = options.Value;
        Distributor = distributor;
        BackgroundJobManager = backgroundJobManager;
        GuidGenerator = guidGenerator;
        Clock = clock;
    }

    public virtual async Task PublishAsync(
        string notificationName,
        NotificationData? data = null,
        NotificationEntityIdentifier? entityIdentifier = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null)
    {
        var notification = new NotificationInfo
        {
            Id = GuidGenerator.Create(),
            NotificationName = notificationName,
            Data = data,
            EntityTypeName = entityIdentifier?.EntityType.FullName,
            EntityId = entityIdentifier?.EntityId.ToString(),
            Severity = severity,
            CreationTime = Clock.Now
        };

        if (userIds != null && userIds.Length <= Options.DirectDistributionUserThreshold)
        {
            await Distributor.DistributeAsync(notification, userIds, excludedUserIds);
        }
        else
        {
            await BackgroundJobManager.EnqueueAsync(
                new NotificationDistributionJobArgs(notification, userIds, excludedUserIds));
        }
    }
}
