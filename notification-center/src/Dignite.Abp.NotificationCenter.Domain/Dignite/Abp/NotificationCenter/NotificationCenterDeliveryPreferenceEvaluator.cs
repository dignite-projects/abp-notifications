using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Persistent, tenant-safe delivery preference evaluator shared by the EF Core and MongoDB providers. Resolves
/// per-user, per-channel opt-outs before work leaves the producing process; delivery is best-effort with no
/// scheduled deferral.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(INotificationDeliveryPreferenceEvaluator))]
public class NotificationCenterDeliveryPreferenceEvaluator :
    INotificationDeliveryPreferenceEvaluator,
    ITransientDependency
{
    protected IRepository<NotificationDeliveryPreference, Guid> PreferenceRepository { get; }

    protected IAsyncQueryableExecuter AsyncExecuter { get; }

    protected IDataFilter DataFilter { get; }

    public NotificationCenterDeliveryPreferenceEvaluator(
        IRepository<NotificationDeliveryPreference, Guid> preferenceRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter)
    {
        PreferenceRepository = preferenceRepository;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
    }

    public virtual async Task<IReadOnlyList<NotificationDeliveryPreferenceDecision>> EvaluateAsync(
        string notificationName,
        Guid? tenantId,
        IReadOnlyCollection<NotificationDeliveryPreferenceCandidate> candidates,
        NotificationDeliveryPreferenceBehavior behavior,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<NotificationDeliveryPreferenceDecision>();
        }

        if (behavior == NotificationDeliveryPreferenceBehavior.Mandatory)
        {
            return candidates
                .Select(candidate => NotificationDeliveryPreferenceDecision.Deliver(
                    candidate.UserId,
                    candidate.Channel))
                .ToList();
        }

        var tenantKey = NotificationDeliveryPreferenceIdentity.GetTenantKey(tenantId);
        var userIds = candidates.Select(candidate => candidate.UserId).Distinct().ToList();
        List<NotificationDeliveryPreference> preferences;
        // The contract requires honoring the supplied tenantId regardless of the caller's ambient tenant, so
        // disable the automatic IMultiTenant filter and rely solely on the explicit non-null TenantKey.
        using (DataFilter.Disable<IMultiTenant>())
        {
            var preferenceQuery = await PreferenceRepository.GetQueryableAsync();
            preferences = await AsyncExecuter.ToListAsync(
                preferenceQuery.Where(preference =>
                    preference.TenantKey == tenantKey && userIds.Contains(preference.UserId)),
                cancellationToken);
        }

        var preferencesByUser = preferences
            .GroupBy(preference => preference.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<NotificationDeliveryPreferenceDecision>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var matchingRule = preferencesByUser.TryGetValue(candidate.UserId, out var userRules)
                ? FindMatchingRule(userRules, notificationName, candidate.Channel)
                : null;
            if (matchingRule is { IsDeliveryEnabled: false })
            {
                result.Add(NotificationDeliveryPreferenceDecision.Suppress(
                    candidate.UserId,
                    candidate.Channel,
                    NotificationDeliveryPreferenceReasonCodes.UserOptOut));
                continue;
            }

            result.Add(NotificationDeliveryPreferenceDecision.Deliver(candidate.UserId, candidate.Channel));
        }

        return result;
    }

    private static NotificationDeliveryPreference? FindMatchingRule(
        IReadOnlyCollection<NotificationDeliveryPreference> rules,
        string notificationName,
        string channel)
    {
        var notificationKey = NotificationDeliveryPreferenceIdentity.GetNotificationNameKey(notificationName);
        var anyNotificationKey = NotificationDeliveryPreferenceIdentity.GetNotificationNameKey(null);
        var channelKey = NotificationDeliveryPreferenceIdentity.GetChannelKey(channel);
        var anyChannelKey = NotificationDeliveryPreferenceIdentity.GetChannelKey(null);

        // Explicit precedence: notification+channel > notification > channel > global > default allow.
        return rules.FirstOrDefault(rule =>
                   rule.NotificationNameKey == notificationKey && rule.ChannelKey == channelKey)
               ?? rules.FirstOrDefault(rule =>
                   rule.NotificationNameKey == notificationKey && rule.ChannelKey == anyChannelKey)
               ?? rules.FirstOrDefault(rule =>
                   rule.NotificationNameKey == anyNotificationKey && rule.ChannelKey == channelKey)
               ?? rules.FirstOrDefault(rule =>
                   rule.NotificationNameKey == anyNotificationKey && rule.ChannelKey == anyChannelKey);
    }
}
