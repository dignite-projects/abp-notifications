using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

public class NotificationPublisher : INotificationPublisher, ITransientDependency
{
    protected NotificationOptions Options { get; }

    protected INotificationDistributer Distributer { get; }

    protected IBackgroundJobManager BackgroundJobManager { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected IClock Clock { get; }

    public NotificationPublisher(
        IOptions<NotificationOptions> options,
        INotificationDistributer distributer,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        Options = options.Value;
        Distributer = distributer;
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

        if (userIds != null && userIds.Length <= Options.MaxUserCountToDirectlyDistributeANotification)
        {
            await Distributer.DistributeAsync(notification, userIds, excludedUserIds);
        }
        else
        {
            await BackgroundJobManager.EnqueueAsync(
                new NotificationDistributionJobArgs(notification, userIds, excludedUserIds));
        }
    }
}
