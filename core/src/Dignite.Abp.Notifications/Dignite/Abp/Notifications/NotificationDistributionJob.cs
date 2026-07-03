using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Runs distribution off the request thread for large or subscription-resolved fan-outs.
/// </summary>
public class NotificationDistributionJob : AsyncBackgroundJob<NotificationDistributionJobArgs>, ITransientDependency
{
    protected INotificationDistributer Distributer { get; }

    public NotificationDistributionJob(INotificationDistributer distributer)
    {
        Distributer = distributer;
    }

    public override Task ExecuteAsync(NotificationDistributionJobArgs args)
    {
        return Distributer.DistributeAsync(args.Notification, args.UserIds, args.ExcludedUserIds);
    }
}
