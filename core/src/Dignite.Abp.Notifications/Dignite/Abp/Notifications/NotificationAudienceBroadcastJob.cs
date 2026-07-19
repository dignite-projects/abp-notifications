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
using Volo.Abp.Timing;

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

    protected INotificationAudienceBroadcastProgressStore ProgressStore { get; }

    protected IClock Clock { get; }

    protected ILogger<NotificationAudienceBroadcastJob> JobLogger { get; }

    protected IHostApplicationLifetime? HostApplicationLifetime { get; }

    public NotificationAudienceBroadcastJob(
        IOptions<NotificationOptions> options,
        IEnumerable<INotificationAudienceRecipientSource> recipientSources,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        ICurrentTenant currentTenant,
        INotificationAudienceBroadcastProgressStore progressStore,
        IClock clock,
        ILogger<NotificationAudienceBroadcastJob> logger)
        : this(options, recipientSources, distributor, backgroundJobManager, currentTenant, progressStore, clock, logger, null)
    {
    }

    public NotificationAudienceBroadcastJob(
        IOptions<NotificationOptions> options,
        IEnumerable<INotificationAudienceRecipientSource> recipientSources,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        ICurrentTenant currentTenant,
        INotificationAudienceBroadcastProgressStore progressStore,
        IClock clock,
        ILogger<NotificationAudienceBroadcastJob> logger,
        IHostApplicationLifetime? hostApplicationLifetime)
    {
        Options = options.Value;
        Options.ValidateDistributionBatching();
        RecipientSources = recipientSources.ToArray();
        Distributor = distributor;
        BackgroundJobManager = backgroundJobManager;
        CurrentTenant = currentTenant;
        ProgressStore = progressStore;
        Clock = clock;
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

        var tags = CreateTags(args);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (CurrentTenant.Change(args.TenantId, null))
            {
                if (await ProgressStore.IsCancellationRequestedAsync(
                        args.Notification.Id,
                        args.TenantId,
                        cancellationToken))
                {
                    await ProgressStore.RecordCanceledAsync(
                        args.Notification,
                        args.AudienceName,
                        args.TenantId,
                        Clock.Now,
                        cancellationToken);
                    JobLogger.LogInformation(
                        "Audience broadcast page {PageIndex} for '{NotificationName}' ({NotificationId}) was " +
                        "skipped because cancellation was requested.",
                        args.PageIndex,
                        args.Notification.NotificationName,
                        args.Notification.Id);
                    return;
                }

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
                    await Distributor.DistributePreparedAsync(
                        args.Notification,
                        recipients,
                        NotificationRecipientEligibilityMode.EnforceDefinitionRequirements,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await ProgressStore.RecordPageCompletedAsync(
                    args.Notification,
                    args.AudienceName,
                    args.TenantId,
                    args.PageIndex,
                    page.UserIds.Count,
                    page.NextCursor,
                    page.HasMore,
                    Clock.Now,
                    cancellationToken);

                if (page.HasMore)
                {
                    if (await ProgressStore.IsCancellationRequestedAsync(
                            args.Notification.Id,
                            args.TenantId,
                            cancellationToken))
                    {
                        await ProgressStore.RecordCanceledAsync(
                            args.Notification,
                            args.AudienceName,
                            args.TenantId,
                            Clock.Now,
                            cancellationToken);
                        return;
                    }

                    await BackgroundJobManager.EnqueueAsync(args.NextPage(page.NextCursor!));
                }
                else
                {
                    await ProgressStore.RecordCompletedAsync(
                        args.Notification,
                        args.AudienceName,
                        args.TenantId,
                        Clock.Now,
                        cancellationToken);
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
            await ProgressStore.RecordFailedAsync(
                args.Notification,
                args.AudienceName,
                args.TenantId,
                "Audience broadcast page failed.",
                Clock.Now,
                CancellationToken.None);
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
