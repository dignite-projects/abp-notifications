using System;
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
        // This is the only place in the pipeline that has to restore a tenant, because it is the only place that
        // runs off the caller's thread. ABP's BackgroundJobExecuter wraps a job in
        // CurrentTenant.Change(GetJobArgsTenantId(args)), which resolves to args.TenantId only for IMultiTenant
        // args — NotificationDistributionJobArgs is not — so the worker thread starts with no ambient tenant.
        // Everything downstream (INotificationStore, INotificationDefinitionManager, INotificationPermissionChecker)
        // reads the ambient tenant, and it flows into their fresh DI scopes because ABP's ICurrentTenantAccessor is
        // an AsyncLocal singleton. Notifiers are NOT downstream of this scope: they handle NotificationDeliveryEto,
        // and ABP's event bus re-enters the tenant from the event itself before invoking them.
        using (CurrentTenant.Change(args.Notification.TenantId, null))
        {
            if (args.RecipientEligibilityMode == NotificationRecipientEligibilityMode.BypassDefinitionRequirements)
            {
                if (args.UserIds == null)
                {
                    throw new ArgumentException(
                        "Definition requirements can only be bypassed for explicit recipients.",
                        nameof(args));
                }

                await Distributor.DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
                    args.Notification,
                    args.UserIds,
                    args.ExcludedUserIds);
            }
            else if (args.RecipientEligibilityMode == NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            {
                await Distributor.DistributeAsync(args.Notification, args.UserIds, args.ExcludedUserIds);
            }
            else
            {
                throw new ArgumentOutOfRangeException(
                    nameof(args),
                    args.RecipientEligibilityMode,
                    "Unknown recipient eligibility mode.");
            }
        }
    }
}
