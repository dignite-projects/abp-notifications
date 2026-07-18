using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationAudienceBroadcaster : INotificationAudienceBroadcaster, ITransientDependency
{
    protected NotificationOptions Options { get; }

    protected INotificationDistributor Distributor { get; }

    protected IBackgroundJobManager BackgroundJobManager { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected IClock Clock { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected INotificationDataTypeRegistry DataTypeRegistry { get; }

    protected INotificationStore Store { get; }

    protected IReadOnlyCollection<INotificationAudienceRecipientSource> RecipientSources { get; }

    protected ILogger<DefaultNotificationAudienceBroadcaster> Logger { get; }

    public DefaultNotificationAudienceBroadcaster(
        IOptions<NotificationOptions> options,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant,
        INotificationDefinitionManager definitionManager,
        INotificationDataTypeRegistry dataTypeRegistry,
        INotificationStore store,
        IEnumerable<INotificationAudienceRecipientSource> recipientSources,
        ILogger<DefaultNotificationAudienceBroadcaster> logger)
    {
        Options = options.Value;
        Options.ValidateDistributionBatching();
        Distributor = distributor;
        BackgroundJobManager = backgroundJobManager;
        GuidGenerator = guidGenerator;
        Clock = clock;
        CurrentTenant = currentTenant;
        DefinitionManager = definitionManager;
        DataTypeRegistry = dataTypeRegistry;
        Store = store;
        RecipientSources = recipientSources.ToArray();
        Logger = logger;
    }

    public virtual async Task<NotificationAudienceBroadcastTenantResult> EnqueueTenantBroadcastAsync(
        NotificationAudienceTenantBroadcastRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAmbientTenantCanTarget(request.TenantId);
        EnsureRecipientSource(request.AudienceName);
        EnsurePreparedDistributor();

        var definition = DefinitionManager.Get(request.NotificationName);
        NotificationDefinitionContractValidator.ValidatePublish(
            definition,
            request.Data,
            request.EntityIdentifier,
            DataTypeRegistry);

        var notification = new NotificationInfo
        {
            Id = GuidGenerator.Create(),
            NotificationName = request.NotificationName,
            Data = request.Data,
            EntityTypeName = request.EntityIdentifier?.EntityTypeName,
            EntityId = request.EntityIdentifier?.EntityId,
            Severity = request.Severity,
            CreationTime = Clock.Now,
            TenantId = request.TenantId
        };

        await InsertNotificationAsync(notification, cancellationToken);
        await BackgroundJobManager.EnqueueAsync(
            new NotificationAudienceBroadcastJobArgs(
                request.TenantId,
                request.AudienceName,
                notification,
                cursor: null,
                pageIndex: 0,
                request.ExcludedUserIds));

        Logger.LogInformation(
            "Enqueued audience broadcast for '{NotificationName}' ({NotificationId}) to audience {AudienceName} " +
            "in tenant {TenantId}.",
            notification.NotificationName,
            notification.Id,
            request.AudienceName,
            request.TenantId);

        return new NotificationAudienceBroadcastTenantResult(
            request.TenantId,
            notification.Id,
            isEnqueued: true);
    }

    public virtual async Task<NotificationAudienceBroadcastResult> EnqueueHostBroadcastAsync(
        NotificationAudienceHostBroadcastRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (CurrentTenant.Id.HasValue)
        {
            throw new AbpException("Host-wide notification audience broadcasts must be started from the host tenant.");
        }

        var results = new List<NotificationAudienceBroadcastTenantResult>(request.TenantIds.Count);
        foreach (var tenantId in request.TenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await EnqueueTenantBroadcastAsync(
                    new NotificationAudienceTenantBroadcastRequest(tenantId, request.NotificationName)
                    {
                        AudienceName = request.AudienceName,
                        Data = request.Data,
                        EntityIdentifier = request.EntityIdentifier,
                        Severity = request.Severity,
                        ExcludedUserIds = request.ExcludedUserIds
                    },
                    cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.LogWarning(
                    exception,
                    "Failed to enqueue audience broadcast for '{NotificationName}' to audience {AudienceName} " +
                    "in tenant {TenantId}.",
                    request.NotificationName,
                    request.AudienceName,
                    tenantId);
                results.Add(new NotificationAudienceBroadcastTenantResult(
                    tenantId,
                    Guid.Empty,
                    isEnqueued: false,
                    exception.Message));
            }
        }

        return new NotificationAudienceBroadcastResult(results);
    }

    protected virtual void EnsureAmbientTenantCanTarget(Guid? tenantId)
    {
        if (CurrentTenant.Id.HasValue && CurrentTenant.Id != tenantId)
        {
            throw new AbpException(
                $"Tenant-scoped notification audience broadcasts cannot target tenant '{tenantId}' from ambient " +
                $"tenant '{CurrentTenant.Id}'.");
        }
    }

    protected virtual INotificationAudienceRecipientSource EnsureRecipientSource(string audienceName)
    {
        var source = RecipientSources.FirstOrDefault(source => source.AudienceName == audienceName);
        if (source == null)
        {
            throw new AbpException($"No notification audience recipient source is registered for '{audienceName}'.");
        }

        return source;
    }

    protected virtual void EnsurePreparedDistributor()
    {
        if (Distributor is not IPreparedNotificationDistributor { SupportsPreparedDistribution: true })
        {
            throw new AbpException(
                "The configured notification distributor cannot process prepared recipient batches required by " +
                "audience broadcasts.");
        }
    }

    protected virtual async Task InsertNotificationAsync(
        NotificationInfo notification,
        CancellationToken cancellationToken)
    {
        if (Store is IBatchedNotificationStore batchedStore)
        {
            await batchedStore.InsertNotificationAsync(notification, cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Store.InsertNotificationAsync(notification);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
