using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Persistent, tenant-safe delivery preference evaluator shared by the EF Core and MongoDB providers.</summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(INotificationDeliveryPreferenceEvaluator))]
public class NotificationCenterDeliveryPreferenceEvaluator :
    INotificationDeliveryPreferenceEvaluator,
    ITransientDependency
{
    protected IRepository<NotificationDeliveryPreference, Guid> PreferenceRepository { get; }

    protected IRepository<NotificationQuietHours, Guid> QuietHoursRepository { get; }

    protected IAsyncQueryableExecuter AsyncExecuter { get; }

    protected IDataFilter DataFilter { get; }

    protected IClock Clock { get; }

    protected ILogger<NotificationCenterDeliveryPreferenceEvaluator> Logger { get; }

    public NotificationCenterDeliveryPreferenceEvaluator(
        IRepository<NotificationDeliveryPreference, Guid> preferenceRepository,
        IRepository<NotificationQuietHours, Guid> quietHoursRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IClock clock,
        ILogger<NotificationCenterDeliveryPreferenceEvaluator> logger)
    {
        PreferenceRepository = preferenceRepository;
        QuietHoursRepository = quietHoursRepository;
        AsyncExecuter = asyncExecuter;
        DataFilter = dataFilter;
        Clock = clock;
        Logger = logger;
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
        List<NotificationQuietHours> quietHours;
        // The contract requires honoring the supplied tenantId regardless of the caller's ambient tenant, so
        // disable the automatic IMultiTenant filter and rely solely on the explicit non-null TenantKey — the same
        // pattern NotificationDeliveryStore uses for its explicit-tenant queries.
        using (DataFilter.Disable<IMultiTenant>())
        {
            var preferenceQuery = await PreferenceRepository.GetQueryableAsync();
            preferences = await AsyncExecuter.ToListAsync(
                preferenceQuery.Where(preference =>
                    preference.TenantKey == tenantKey && userIds.Contains(preference.UserId)),
                cancellationToken);
            var quietHoursQuery = await QuietHoursRepository.GetQueryableAsync();
            quietHours = await AsyncExecuter.ToListAsync(
                quietHoursQuery.Where(schedule =>
                    schedule.TenantKey == tenantKey && userIds.Contains(schedule.UserId)),
                cancellationToken);
        }
        var preferencesByUser = preferences
            .GroupBy(preference => preference.UserId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var quietHoursByUser = quietHours.ToDictionary(schedule => schedule.UserId);
        var now = NormalizeUtc(Clock.Now);
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

            if (quietHoursByUser.TryGetValue(candidate.UserId, out var schedule)
                && TryGetQuietHoursEndSafe(now, schedule, out var notBefore))
            {
                result.Add(NotificationDeliveryPreferenceDecision.Delay(
                    candidate.UserId,
                    candidate.Channel,
                    notBefore,
                    NotificationDeliveryPreferenceReasonCodes.QuietHours));
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

    private bool TryGetQuietHoursEndSafe(DateTime utcNow, NotificationQuietHours schedule, out DateTime utcEnd)
    {
        try
        {
            return TryGetQuietHoursEnd(utcNow, schedule, out utcEnd);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // The id was validated when the schedule was saved, so failure here means environment drift (tz data
            // removed or renamed on this host). Fail open to "no quiet hours" for this evaluation instead of
            // throwing the whole candidate batch away. No recipient ids in the log (invariants §9).
            Logger.LogWarning(
                "Ignoring a quiet-hours schedule with unresolvable time zone '{TimeZoneId}'; delivering immediately.",
                schedule.TimeZoneId);
            utcEnd = default;
            return false;
        }
    }

    internal static bool TryGetQuietHoursEnd(
        DateTime utcNow,
        NotificationQuietHours schedule,
        out DateTime utcEnd)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(utcNow), timeZone);
        var minute = localNow.Hour * 60 + localNow.Minute;
        var spansMidnight = schedule.StartMinute > schedule.EndMinute;
        var isQuiet = spansMidnight
            ? minute >= schedule.StartMinute || minute < schedule.EndMinute
            : minute >= schedule.StartMinute && minute < schedule.EndMinute;
        if (!isQuiet)
        {
            utcEnd = default;
            return false;
        }

        var endDate = localNow.Date;
        if (spansMidnight && minute >= schedule.StartMinute)
        {
            endDate = endDate.AddDays(1);
        }

        var localEnd = DateTime.SpecifyKind(
            endDate.AddMinutes(schedule.EndMinute),
            DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(localEnd))
        {
            localEnd = localEnd.AddMinutes(1);
        }

        utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone);
        return true;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
