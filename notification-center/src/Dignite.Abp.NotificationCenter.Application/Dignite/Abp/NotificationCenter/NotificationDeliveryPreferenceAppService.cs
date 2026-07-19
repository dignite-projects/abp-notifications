using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Dignite.Abp.NotificationCenter;

[Authorize]
public class NotificationDeliveryPreferenceAppService :
    ApplicationService,
    INotificationDeliveryPreferenceAppService
{
    protected NotificationDeliveryPreferenceManager PreferenceManager { get; }

    protected IRepository<NotificationDeliveryPreference, Guid> PreferenceRepository { get; }

    protected IRepository<NotificationQuietHours, Guid> QuietHoursRepository { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    public NotificationDeliveryPreferenceAppService(
        NotificationDeliveryPreferenceManager preferenceManager,
        IRepository<NotificationDeliveryPreference, Guid> preferenceRepository,
        IRepository<NotificationQuietHours, Guid> quietHoursRepository,
        INotificationDefinitionManager definitionManager)
    {
        PreferenceManager = preferenceManager;
        PreferenceRepository = preferenceRepository;
        QuietHoursRepository = quietHoursRepository;
        DefinitionManager = definitionManager;
    }

    public virtual async Task<ListResultDto<NotificationDeliveryPreferenceDto>> GetListAsync()
    {
        var tenantKey = NotificationDeliveryPreferenceIdentity.GetTenantKey(CurrentTenant.Id);
        var userId = CurrentUser.GetId();
        var preferences = await PreferenceRepository.GetListAsync(
            preference => preference.TenantKey == tenantKey && preference.UserId == userId);
        return new ListResultDto<NotificationDeliveryPreferenceDto>(preferences
            .OrderBy(preference => preference.NotificationName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(preference => preference.Channel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(MapPreference)
            .ToList());
    }

    public virtual async Task<NotificationDeliveryPreferenceDto> SetPreferenceAsync(
        SetNotificationDeliveryPreferenceDto input)
    {
        var scope = ValidateScope(input.NotificationName, input.Channel);
        var preference = await PreferenceManager.SetPreferenceAsync(
            CurrentUser.GetId(),
            scope.NotificationName,
            scope.Channel,
            input.IsDeliveryEnabled);
        return MapPreference(preference);
    }

    public virtual Task DeleteAsync(DeleteNotificationDeliveryPreferenceDto input)
    {
        // Unlike SetPreferenceAsync, deleting must not consult the definition catalog: a stored rule whose notification
        // definition was later renamed or removed would otherwise be undeletable forever. The manager computes
        // the same normalized identity keys from the raw scope, so this targets exactly the row SetPreference created.
        return PreferenceManager.DeleteAsync(CurrentUser.GetId(), input.NotificationName, input.Channel);
    }

    public virtual async Task<NotificationQuietHoursDto?> GetQuietHoursAsync()
    {
        var id = NotificationDeliveryPreferenceIdentity.CreateQuietHoursId(
            CurrentTenant.Id,
            CurrentUser.GetId());
        var schedule = await QuietHoursRepository.FindAsync(id);
        return schedule == null ? null : MapQuietHours(schedule);
    }

    public virtual async Task<NotificationQuietHoursDto> SetQuietHoursAsync(SetNotificationQuietHoursDto input)
    {
        var schedule = await PreferenceManager.SetQuietHoursAsync(
            CurrentUser.GetId(),
            input.StartMinute,
            input.EndMinute,
            input.TimeZoneId);
        return MapQuietHours(schedule);
    }

    public virtual Task DeleteQuietHoursAsync()
    {
        return PreferenceManager.DeleteQuietHoursAsync(CurrentUser.GetId());
    }

    protected virtual (string? NotificationName, string? Channel) ValidateScope(
        string? notificationName,
        string? channel)
    {
        var normalizedNotificationName = notificationName?.Trim();
        if (normalizedNotificationName != null)
        {
            _ = DefinitionManager.Get(normalizedNotificationName);
        }

        var normalizedChannel = channel == null
            ? null
            : NotificationDeliveryIdentity.NormalizeChannel(channel);
        if (normalizedNotificationName != null && normalizedChannel != null)
        {
            var definitionChannels = DefinitionManager.Get(normalizedNotificationName).GetChannelsOrNull();
            if (definitionChannels == null || !definitionChannels.Contains(
                    normalizedChannel,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Channel '{channel}' is not configured for notification '{normalizedNotificationName}'.",
                    nameof(channel));
            }
        }

        return (normalizedNotificationName, normalizedChannel);
    }

    private static NotificationDeliveryPreferenceDto MapPreference(NotificationDeliveryPreference preference)
    {
        return new NotificationDeliveryPreferenceDto
        {
            NotificationName = preference.NotificationName,
            Channel = preference.Channel,
            IsDeliveryEnabled = preference.IsDeliveryEnabled
        };
    }

    private static NotificationQuietHoursDto MapQuietHours(NotificationQuietHours schedule)
    {
        return new NotificationQuietHoursDto
        {
            StartMinute = schedule.StartMinute,
            EndMinute = schedule.EndMinute,
            TimeZoneId = schedule.TimeZoneId
        };
    }
}
