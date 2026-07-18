using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Pages a tenant-scoped audience into the standard prepared notification distribution pipeline.
/// </summary>
public class NotificationAudienceBroadcastJob :
    AsyncBackgroundJob<NotificationAudienceBroadcastJobArgs>,
    ITransientDependency
{
    protected NotificationOptions Options { get; }

    protected IReadOnlyCollection<INotificationAudienceRecipientSource> RecipientSources { get; }

    protected INotificationDistributor Distributor { get; }

    protected IBackgroundJobManager BackgroundJobManager { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected ILogger<NotificationAudienceBroadcastJob> JobLogger { get; }

    protected IHostApplicationLifetime? HostApplicationLifetime { get; }

    public NotificationAudienceBroadcastJob(
        IOptions<NotificationOptions> options,
        IEnumerable<INotificationAudienceRecipientSource> recipientSources,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        ICurrentTenant currentTenant,
        ILogger<NotificationAudienceBroadcastJob> logger)
        : this(options, recipientSources, distributor, backgroundJobManager, currentTenant, logger, null)
    {
    }

    public NotificationAudienceBroadcastJob(
        IOptions<NotificationOptions> options,
        IEnumerable<INotificationAudienceRecipientSource> recipientSources,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        ICurrentTenant currentTenant,
        ILogger<NotificationAudienceBroadcastJob> logger,
        IHostApplicationLifetime? hostApplicationLifetime)
    {
        Options = options.Value;
        Options.ValidateDistributionBatching();
        RecipientSources = recipientSources.ToArray();
        Distributor = distributor;
        BackgroundJobManager = backgroundJobManager;
        CurrentTenant = currentTenant;
        JobLogger = logger;
        HostApplicationLifetime = hostApplicationLifetime;
    }

    public override Task ExecuteAsync(NotificationAudienceBroadcastJobArgs args)
    {
        return ExecuteAsync(
            args,
            HostApplicationLifetime?.ApplicationStopping ?? CancellationToken.None);
    }

    /// <summary>
    /// Executes one audience page. Progress is represented by the stable notification id, tenant id, page index,
    /// and cursor on <paramref name="args"/>; cancellation is honored through the supplied token.
    /// </summary>
    public virtual async Task ExecuteAsync(
        NotificationAudienceBroadcastJobArgs args,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(args.Notification);
        if (args.Notification.TenantId != args.TenantId)
        {
            throw new AbpException(
                "Audience broadcast job tenant id must match the prepared notification tenant id.");
        }

        var source = RecipientSources.FirstOrDefault(source => source.AudienceName == args.AudienceName);
        if (source == null)
        {
            throw new AbpException(
                $"No notification audience recipient source is registered for '{args.AudienceName}'.");
        }

        if (Distributor is not IPreparedNotificationDistributor
            {
                SupportsPreparedDistribution: true
            } preparedDistributor)
        {
            throw new AbpException(
                "The configured notification distributor cannot process prepared recipient batches required by " +
                "audience broadcasts.");
        }

        var tags = CreateTags(args);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (CurrentTenant.Change(args.TenantId, null))
            {
                var page = await source.GetRecipientsAsync(
                    new NotificationAudienceRecipientPageRequest(
                        args.AudienceName,
                        args.TenantId,
                        args.Cursor,
                        Options.RecipientBatchSize),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (page.UserIds.Count > Options.RecipientBatchSize)
                {
                    throw new AbpException(
                        $"Audience recipient source '{args.AudienceName}' returned {page.UserIds.Count} recipients, " +
                        $"exceeding the configured page size {Options.RecipientBatchSize}.");
                }

                if (page.HasMore && string.IsNullOrWhiteSpace(page.NextCursor))
                {
                    throw new AbpException(
                        $"Audience recipient source '{args.AudienceName}' reported more recipients without a cursor.");
                }

                NotificationAudienceBroadcastMetrics.PageCount.Add(1, tags);
                NotificationAudienceBroadcastMetrics.CandidateCount.Add(page.UserIds.Count, tags);
                var recipients = BoundedRecipientBatcher.RemoveExcludedRecipients(
                    page.UserIds.Distinct().ToArray(),
                    args.ExcludedUserIds);

                if (recipients.Length > 0)
                {
                    await preparedDistributor.DistributePreparedAsync(
                        args.Notification,
                        recipients,
                        NotificationRecipientEligibilityMode.EnforceDefinitionRequirements,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (page.HasMore)
                {
                    await BackgroundJobManager.EnqueueAsync(args.NextPage(page.NextCursor!));
                }

                JobLogger.LogInformation(
                    "Processed audience broadcast page {PageIndex} for '{NotificationName}' ({NotificationId}) " +
                    "to audience {AudienceName} in tenant {TenantId}: {CandidateCount} candidates, " +
                    "{RecipientCount} prepared recipients, has more: {HasMore}.",
                    args.PageIndex,
                    args.Notification.NotificationName,
                    args.Notification.Id,
                    args.AudienceName,
                    args.TenantId,
                    page.UserIds.Count,
                    recipients.Length,
                    page.HasMore);
            }
        }
        catch (OperationCanceledException)
        {
            JobLogger.LogInformation(
                "Audience broadcast page {PageIndex} for '{NotificationName}' ({NotificationId}) was canceled.",
                args.PageIndex,
                args.Notification.NotificationName,
                args.Notification.Id);
            throw;
        }
        catch (Exception)
        {
            NotificationAudienceBroadcastMetrics.FailureCount.Add(1, tags);
            throw;
        }
    }

    protected virtual TagList CreateTags(NotificationAudienceBroadcastJobArgs args)
    {
        var tags = new TagList
        {
            { "notification.name", args.Notification.NotificationName },
            { "audience.name", args.AudienceName },
            { "tenant.scope", args.TenantId.HasValue ? "tenant" : "host" }
        };

        return tags;
    }
}
