using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Abp.NotificationCenter;

public class NotificationDeliveryPreferenceManager :
    INotificationDeliveryPreferenceManager,
    ITransientDependency
{
    protected IRepository<NotificationDeliveryPreference, Guid> PreferenceRepository { get; }

    protected IRepository<NotificationQuietHours, Guid> QuietHoursRepository { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected IClock Clock { get; }

    public NotificationDeliveryPreferenceManager(
        IRepository<NotificationDeliveryPreference, Guid> preferenceRepository,
        IRepository<NotificationQuietHours, Guid> quietHoursRepository,
        ICurrentTenant currentTenant,
        IClock clock)
    {
        PreferenceRepository = preferenceRepository;
        QuietHoursRepository = quietHoursRepository;
        CurrentTenant = currentTenant;
        Clock = clock;
    }

    public virtual Task<List<NotificationDeliveryPreference>> GetListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var tenantKey = NotificationDeliveryPreferenceIdentity.GetTenantKey(CurrentTenant.Id);
        return PreferenceRepository.GetListAsync(
            preference => preference.TenantKey == tenantKey && preference.UserId == userId,
            cancellationToken: cancellationToken);
    }

    public virtual async Task<NotificationDeliveryPreference> SetAsync(
        Guid userId,
        string? notificationName,
        string? channel,
        bool isEnabled,
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
                isEnabled,
                Clock.Now,
                CurrentTenant.Id);
            return await PreferenceRepository.InsertAsync(
                preference,
                autoSave: true,
                cancellationToken: cancellationToken);
        }

        preference.SetEnabled(isEnabled, Clock.Now);
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

    public virtual Task<NotificationQuietHours?> GetQuietHoursOrNullAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var id = NotificationDeliveryPreferenceIdentity.CreateQuietHoursId(CurrentTenant.Id, userId);
        return QuietHoursRepository.FindAsync(id, cancellationToken: cancellationToken);
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
