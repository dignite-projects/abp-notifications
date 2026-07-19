using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Validates identities and mutates permanent delivery preferences and quiet-hours schedules.</summary>
public class NotificationDeliveryPreferenceManager : DomainService
{
    protected IRepository<NotificationDeliveryPreference, Guid> PreferenceRepository { get; }

    protected IRepository<NotificationQuietHours, Guid> QuietHoursRepository { get; }

    public NotificationDeliveryPreferenceManager(
        IRepository<NotificationDeliveryPreference, Guid> preferenceRepository,
        IRepository<NotificationQuietHours, Guid> quietHoursRepository)
    {
        PreferenceRepository = preferenceRepository;
        QuietHoursRepository = quietHoursRepository;
    }

    public virtual async Task<NotificationDeliveryPreference> SetPreferenceAsync(
        Guid userId,
        string? notificationName,
        string? channel,
        bool isDeliveryEnabled,
        CancellationToken cancellationToken = default)
    {
        var id = NotificationDeliveryPreferenceIdentity.CreatePreferenceId(
            CurrentTenant.Id,
            userId,
            notificationName,
            channel);
        var preference = await PreferenceRepository.FindAsync(id, cancellationToken: cancellationToken);
        if (preference == null)
        {
            preference = new NotificationDeliveryPreference(
                id,
                userId,
                notificationName,
                channel,
                isDeliveryEnabled,
                Clock.Now,
                CurrentTenant.Id);
            return await PreferenceRepository.InsertAsync(
                preference,
                autoSave: true,
                cancellationToken: cancellationToken);
        }

        preference.SetDeliveryEnabled(isDeliveryEnabled, Clock.Now);
        return await PreferenceRepository.UpdateAsync(
            preference,
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    public virtual async Task DeleteAsync(
        Guid userId,
        string? notificationName,
        string? channel,
        CancellationToken cancellationToken = default)
    {
        var id = NotificationDeliveryPreferenceIdentity.CreatePreferenceId(
            CurrentTenant.Id,
            userId,
            notificationName,
            channel);
        var preference = await PreferenceRepository.FindAsync(id, cancellationToken: cancellationToken);
        if (preference != null)
        {
            await PreferenceRepository.DeleteAsync(preference, autoSave: true, cancellationToken: cancellationToken);
        }
    }

    public virtual async Task<NotificationQuietHours> SetQuietHoursAsync(
        Guid userId,
        int startMinute,
        int endMinute,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        var id = NotificationDeliveryPreferenceIdentity.CreateQuietHoursId(CurrentTenant.Id, userId);
        var quietHours = await QuietHoursRepository.FindAsync(id, cancellationToken: cancellationToken);
        if (quietHours == null)
        {
            quietHours = new NotificationQuietHours(
                id,
                userId,
                startMinute,
                endMinute,
                timeZoneId,
                Clock.Now,
                CurrentTenant.Id);
            return await QuietHoursRepository.InsertAsync(
                quietHours,
                autoSave: true,
                cancellationToken: cancellationToken);
        }

        quietHours.Set(startMinute, endMinute, timeZoneId, Clock.Now);
        return await QuietHoursRepository.UpdateAsync(
            quietHours,
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    public virtual async Task DeleteQuietHoursAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var id = NotificationDeliveryPreferenceIdentity.CreateQuietHoursId(CurrentTenant.Id, userId);
        var quietHours = await QuietHoursRepository.FindAsync(id, cancellationToken: cancellationToken);
        if (quietHours != null)
        {
            await QuietHoursRepository.DeleteAsync(quietHours, autoSave: true, cancellationToken: cancellationToken);
        }
    }
}
