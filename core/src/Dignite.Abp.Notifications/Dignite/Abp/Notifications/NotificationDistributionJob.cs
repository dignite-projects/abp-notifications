using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
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

    protected IHostApplicationLifetime? HostApplicationLifetime { get; }

    public NotificationDistributionJob(
        INotificationDistributor distributor,
        ICurrentTenant currentTenant)
        : this(distributor, currentTenant, null)
    {
    }

    public NotificationDistributionJob(
        INotificationDistributor distributor,
        ICurrentTenant currentTenant,
        IHostApplicationLifetime? hostApplicationLifetime)
    {
        Distributor = distributor;
        CurrentTenant = currentTenant;
        HostApplicationLifetime = hostApplicationLifetime;
    }

    public override Task ExecuteAsync(NotificationDistributionJobArgs args)
    {
        return ExecuteAsync(
            args,
            HostApplicationLifetime?.ApplicationStopping ?? CancellationToken.None);
    }

    /// <summary>
    /// Executes with explicit cancellation. ABP's background-job contract has no token parameter, so the normal
    /// override supplies the host shutdown token; direct/custom job runners can pass their own token.
    /// </summary>
    public virtual async Task ExecuteAsync(
        NotificationDistributionJobArgs args,
        CancellationToken cancellationToken)
    {
        // Restore the tenant before invoking even a custom distributor. The default distributor independently
        // scopes its whole operation to Notification.TenantId so direct calls cannot mix tenant/host data either.
        // ABP's BackgroundJobExecuter wraps a job in
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

                if (Distributor is ICancellableNotificationDistributor cancellableDistributor)
                {
                    await cancellableDistributor.DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
                        args.Notification,
                        args.UserIds,
                        args.ExcludedUserIds,
                        cancellationToken);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Distributor.DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
                        args.Notification,
                        args.UserIds,
                        args.ExcludedUserIds);
                }
            }
            else if (args.RecipientEligibilityMode == NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            {
                if (Distributor is ICancellableNotificationDistributor cancellableDistributor)
                {
                    await cancellableDistributor.DistributeAsync(
                        args.Notification,
                        args.UserIds,
                        args.ExcludedUserIds,
                        cancellationToken);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Distributor.DistributeAsync(
                        args.Notification,
                        args.UserIds,
                        args.ExcludedUserIds);
                }
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
