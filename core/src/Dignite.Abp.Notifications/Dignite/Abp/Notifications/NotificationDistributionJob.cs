using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Runs distribution off the request thread for large or subscription-resolved fan-outs.
/// </summary>
public class NotificationDistributionJob : AsyncBackgroundJob<NotificationDistributionJobArgs>, ITransientDependency
{
    protected INotificationDistributor Distributor { get; }

    protected ICurrentTenant CurrentTenant { get; }

    public NotificationDistributionJob(
        INotificationDistributor distributor,
        ICurrentTenant currentTenant)
    {
        Distributor = distributor;
        CurrentTenant = currentTenant;
    }

    public override async Task ExecuteAsync(NotificationDistributionJobArgs args)
    {
        using (CurrentTenant.Change(args.Notification.TenantId, null))
        {
            await Distributor.DistributeAsync(args.Notification, args.UserIds, args.ExcludedUserIds);
        }
    }
}
