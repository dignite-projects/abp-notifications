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
using Volo.Abp.Uow;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationAudienceBroadcaster : INotificationAudienceBroadcaster, ITransientDependency
{
    protected NotificationAudienceBroadcastOptions Options { get; }

    protected INotificationDistributor Distributor { get; }

    protected IBackgroundJobManager BackgroundJobManager { get; }

    protected IGuidGenerator GuidGenerator { get; }

    protected IClock Clock { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected INotificationDataTypeRegistry DataTypeRegistry { get; }

    protected INotificationStore Store { get; }

    protected IReadOnlyCollection<INotificationAudienceRecipientSource> RecipientSources { get; }

    protected INotificationAudienceBroadcastProgressStore ProgressStore { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    protected ILogger<DefaultNotificationAudienceBroadcaster> Logger { get; }

    public DefaultNotificationAudienceBroadcaster(
        IOptions<NotificationAudienceBroadcastOptions> options,
        INotificationDistributor distributor,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IClock clock,
        ICurrentTenant currentTenant,
        INotificationDefinitionManager definitionManager,
        INotificationDataTypeRegistry dataTypeRegistry,
        INotificationStore store,
        IEnumerable<INotificationAudienceRecipientSource> recipientSources,
        INotificationAudienceBroadcastProgressStore progressStore,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<DefaultNotificationAudienceBroadcaster> logger)
    {
        Options = options.Value;
        Options.Validate();
        Distributor = distributor;
        BackgroundJobManager = backgroundJobManager;
        GuidGenerator = guidGenerator;
        Clock = clock;
        CurrentTenant = currentTenant;
        DefinitionManager = definitionManager;
        DataTypeRegistry = dataTypeRegistry;
        Store = store;
        RecipientSources = recipientSources.ToArray();
        ProgressStore = progressStore;
        UnitOfWorkManager = unitOfWorkManager;
        Logger = logger;
    }

    public virtual async Task<NotificationAudienceBroadcastEnqueueResult> EnqueueAsync(
        NotificationAudienceBroadcastRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAmbientTenantCanTargetScope(request.TenantId);
        EnsureRecipientSource(request.AudienceName);

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
                continuationToken: null,
                pageIndex: 0,
                request.ExcludedUserIds));
        await ProgressStore.RecordStartedAsync(
            notification,
            request.AudienceName,
            request.TenantId,
            Clock.Now,
            cancellationToken);

        Logger.LogInformation(
            "Enqueued audience broadcast for '{NotificationName}' ({NotificationId}) to audience {AudienceName} " +
            "in tenant-or-host scope {TenantId}.",
            notification.NotificationName,
            notification.Id,
            request.AudienceName,
            request.TenantId);

        return new NotificationAudienceBroadcastEnqueueResult(
            request.TenantId,
            notification.Id,
            isEnqueued: true);
    }

    public virtual async Task<NotificationAudienceMultiTenantBroadcastResult> EnqueueForTenantsAsync(
        NotificationAudienceMultiTenantBroadcastRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (CurrentTenant.Id.HasValue)
        {
            throw new AbpException("Multi-tenant notification audience broadcasts must be started from the host scope.");
        }

        var results = new List<NotificationAudienceBroadcastEnqueueResult>(request.TenantIds.Count);
        foreach (var tenantId in request.TenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
                var tenantResult = await EnqueueAsync(
                    new NotificationAudienceBroadcastRequest(tenantId, request.NotificationName)
                    {
                        AudienceName = request.AudienceName,
                        Data = request.Data,
                        EntityIdentifier = request.EntityIdentifier,
                        Severity = request.Severity,
                        ExcludedUserIds = request.ExcludedUserIds
                    },
                    cancellationToken);
                await unitOfWork.CompleteAsync(cancellationToken);
                results.Add(tenantResult);
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
                results.Add(new NotificationAudienceBroadcastEnqueueResult(
                    tenantId,
                    Guid.Empty,
                    isEnqueued: false,
                    exception.Message));
            }
        }

        return new NotificationAudienceMultiTenantBroadcastResult(results);
    }

    public virtual Task<NotificationAudienceBroadcastProgress?> GetProgressAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        ValidateNotificationId(notificationId);
        ValidateScopeTenantId(tenantId);
        EnsureAmbientTenantCanTargetScope(tenantId);
        return ProgressStore.GetAsync(notificationId, tenantId, cancellationToken);
    }

    public virtual Task<bool> CancelAsync(
        Guid notificationId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        ValidateNotificationId(notificationId);
        ValidateScopeTenantId(tenantId);
        EnsureAmbientTenantCanTargetScope(tenantId);
        return ProgressStore.RequestCancellationAsync(
            notificationId,
            tenantId,
            Clock.Now,
            cancellationToken);
    }

    protected virtual void EnsureAmbientTenantCanTargetScope(Guid? tenantId)
    {
        ValidateScopeTenantId(tenantId);
        if (CurrentTenant.Id.HasValue && CurrentTenant.Id != tenantId)
        {
            throw new AbpException(
                $"A notification audience broadcast cannot target scope tenant '{tenantId}' from ambient " +
                $"tenant '{CurrentTenant.Id}'.");
        }
    }

    protected virtual void ValidateScopeTenantId(Guid? tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be Guid.Empty. Use null for the host scope.", nameof(tenantId));
        }
    }

    protected virtual void ValidateNotificationId(Guid notificationId)
    {
        if (notificationId == Guid.Empty)
        {
            throw new ArgumentException("Notification id cannot be Guid.Empty.", nameof(notificationId));
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

    protected virtual async Task InsertNotificationAsync(
        NotificationInfo notification,
        CancellationToken cancellationToken)
    {
        await Store.InsertNotificationAsync(notification, cancellationToken);
    }
}
