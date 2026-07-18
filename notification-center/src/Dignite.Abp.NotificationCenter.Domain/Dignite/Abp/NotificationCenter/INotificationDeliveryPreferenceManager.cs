using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.NotificationCenter;

public interface INotificationDeliveryPreferenceManager
{
    Task<List<NotificationDeliveryPreference>> GetListAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<NotificationDeliveryPreference> SetAsync(
        Guid userId,
        string? notificationName,
        string? channel,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid userId,
        string? notificationName,
        string? channel,
        CancellationToken cancellationToken = default);

    Task<NotificationQuietHours?> GetQuietHoursOrNullAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<NotificationQuietHours> SetQuietHoursAsync(
        Guid userId,
        int startMinute,
        int endMinute,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    Task DeleteQuietHoursAsync(Guid userId, CancellationToken cancellationToken = default);
}
