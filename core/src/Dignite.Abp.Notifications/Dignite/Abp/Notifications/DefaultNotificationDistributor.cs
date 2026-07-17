using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationDistributor : INotificationDistributor, ITransientDependency
{
    protected INotificationStore Store { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    protected IDistributedEventBus DistributedEventBus { get; }

    protected INotificationRecipientEligibilityEvaluator RecipientEligibilityEvaluator { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected ILogger<DefaultNotificationDistributor> Logger { get; }

    protected INotificationDataTypeRegistry DataTypeRegistry { get; }

    public DefaultNotificationDistributor(
        INotificationStore store,
        INotificationDefinitionManager definitionManager,
        IDistributedEventBus distributedEventBus,
        INotificationRecipientEligibilityEvaluator recipientEligibilityEvaluator,
        ICurrentTenant currentTenant,
        ILogger<DefaultNotificationDistributor> logger,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        Store = store;
        DefinitionManager = definitionManager;
        DistributedEventBus = distributedEventBus;
        RecipientEligibilityEvaluator = recipientEligibilityEvaluator;
        CurrentTenant = currentTenant;
        Logger = logger;
        DataTypeRegistry = dataTypeRegistry;
    }

    public virtual Task DistributeAsync(
        NotificationInfo notification, Guid[]? userIds = null, Guid[]? excludedUserIds = null)
    {
        return DistributeAsyncInternal(
            notification,
            userIds,
            excludedUserIds,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);
    }

    public virtual Task DistributeToExplicitRecipientsWithoutEligibilityChecksAsync(
        NotificationInfo notification,
        Guid[] userIds,
        Guid[]? excludedUserIds = null)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        return DistributeAsyncInternal(
            notification,
            userIds,
            excludedUserIds,
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements);
    }

    protected virtual async Task DistributeAsyncInternal(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds,
        NotificationRecipientEligibilityMode recipientEligibilityMode)
    {
        // An empty, explicitly supplied recipient list is intentionally different from null. Return before
        // subscription lookup or channel validation so every direct/background path remains a true no-op.
        if (userIds is { Length: 0 })
        {
            return;
        }

        var definition = DefinitionManager.Get(notification.NotificationName);
        NotificationDefinitionContractValidator.ValidateDistribution(
            definition,
            notification,
            DataTypeRegistry);

        using (CurrentTenant.Change(notification.TenantId, null))
        {
            if (recipientEligibilityMode == NotificationRecipientEligibilityMode.BypassDefinitionRequirements)
            {
                if (userIds == null)
                {
                    throw new ArgumentException(
                        "Definition requirements can only be bypassed for explicit recipients.",
                        nameof(userIds));
                }

                // Audit outside the replaceable evaluator so a custom policy cannot make a trusted-system bypass
                // invisible. An explicitly empty list returned before entering this scope and remains a true no-op.
                Logger.LogWarning(
                    "Bypassing notification definition requirements for {RecipientCount} explicit recipients of " +
                    "'{NotificationName}' in tenant {TenantId}.",
                    userIds.Distinct().Count(),
                    notification.NotificationName,
                    notification.TenantId);
            }
            else if (recipientEligibilityMode != NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(recipientEligibilityMode),
                    recipientEligibilityMode,
                    "Unknown recipient eligibility mode.");
            }

            var channels = ResolveExternalChannelsOrNull(notification.NotificationName);
            var candidateUserIds = (await GetTargetUserIdsAsync(notification, userIds, excludedUserIds))
                .Distinct()
                .ToList();
            if (candidateUserIds.Count == 0)
            {
                return;
            }

            // Eligibility deliberately runs after the established candidate-selection extension point so custom
            // distributor subclasses cannot accidentally restore the historical explicit-recipient bypass.
            var evaluation = await RecipientEligibilityEvaluator.EvaluateAsync(
                notification.NotificationName,
                candidateUserIds,
                notification.TenantId,
                recipientEligibilityMode);
            var targetUserIds = evaluation.EligibleUserIds.Distinct().ToList();
            if (targetUserIds.Count == 0)
            {
                return;
            }

            // These two commit together only when the host enables ABP's transactional outbox — with NotificationCenter
            // on EF Core that is Configure<AbpDistributedEventBusOptions>(o => o.UseNotificationCenterEfCoreOutbox()).
            // Without it, and always on the MongoDB provider (which wires no outbox), a crash between the two keeps the
            // rows and drops the event. Notification_Outbox_Tests covers the case where the guarantee does hold.
            await SaveUserNotificationsAsync(notification, targetUserIds);
            if (channels != null)
            {
                await PublishNotificationDeliveryAsync(notification, targetUserIds, channels);
            }
        }
    }

    protected virtual string[]? ResolveExternalChannelsOrNull(string notificationName)
    {
        var definition = DefinitionManager.Get(notificationName);

        var channels = definition.GetChannelsOrNull();
        if (channels == null)
        {
            if (Store is NullNotificationStore)
            {
                throw new AbpException(
                    $"Notification '{notificationName}' has no external channels and no NotificationCenter inbox store is installed. Configure UseChannels(...) or install NotificationCenter.");
            }

            return null;
        }

        if (channels.Length == 0 || channels.Any(string.IsNullOrWhiteSpace))
        {
            throw new AbpException(
                $"Notification '{notificationName}' has invalid delivery channel configuration.");
        }

        return channels;
    }

    protected virtual async Task<List<Guid>> GetTargetUserIdsAsync(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds)
    {
        List<Guid> candidates;

        if (userIds != null)
        {
            candidates = userIds.Distinct().ToList();
        }
        else
        {
            var subscriptions = await Store.GetSubscriptionsAsync(
                notification.NotificationName, notification.EntityTypeName, notification.EntityId);
            candidates = subscriptions.Select(subscription => subscription.UserId).Distinct().ToList();
        }

        if (excludedUserIds != null && excludedUserIds.Length > 0)
        {
            var excluded = new HashSet<Guid>(excludedUserIds);
            candidates = candidates.Where(userId => !excluded.Contains(userId)).ToList();
        }

        return candidates;
    }

    protected virtual async Task SaveUserNotificationsAsync(NotificationInfo notification, List<Guid> userIds)
    {
        await Store.InsertNotificationAsync(notification);

        foreach (var userId in userIds)
        {
            await Store.InsertUserNotificationAsync(new UserNotificationInfo
            {
                UserId = userId,
                NotificationId = notification.Id,
                State = UserNotificationState.Unread,
                CreationTime = notification.CreationTime,
                TenantId = notification.TenantId
            });
        }
    }

    protected virtual Task PublishNotificationDeliveryAsync(NotificationInfo notification, List<Guid> userIds, string[] channels)
    {
        // The ETO carries the full recipient list for notifier routing; notifiers trim it per user before pushing.
        var eto = new NotificationDeliveryEto(
            notification.Id,
            notification.NotificationName,
            notification.Data,
            notification.Severity,
            notification.CreationTime,
            userIds.ToArray())
        {
            Channels = channels,
            TenantId = notification.TenantId,
            EntityTypeName = notification.EntityTypeName,
            EntityId = notification.EntityId
        };

        return DistributedEventBus.PublishAsync(eto);
    }
}
